# ============================================================
# OmniSift.Api — Multi-stage Docker Build
# Stage 1 (model-fetch): Download the bge-small-en-v1.5 ONNX model
# Stage 2 (build):       Build + publish the .NET app
# Stage 3 (runtime):     Minimal ASP.NET runtime image
# ============================================================

# -- Model-Fetch Stage
# Isolated so the ~127 MB download is cached independently from both the
# SDK and the source build layers: edits to src/ never re-download it.
#
# This stage is REQUIRED, not optional. appsettings.json ships
# Embedding:Provider=Onnx, and Program.cs resolves IEmbeddingService during
# startup specifically so a missing model fails fast. Without the model in
# the image the API throws FileNotFoundException before it serves a single
# request. (Regression history: this stage was dropped when the Dockerfile
# was rewritten, which is why DockerModelPackagingTests now guards it.)
FROM debian:bookworm-slim AS model-fetch

# curl + coreutils (sha256sum) — install and clean up in one layer.
# ca-certificates is NOT optional: debian:bookworm-slim ships no CA bundle, so
# without it every HTTPS fetch dies with `curl: (77) error setting certificate
# file` before a single byte is downloaded.
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl coreutils \
    && rm -rf /var/lib/apt/lists/*

# Must stay in sync with Embedding:ModelPath / Embedding:TokenizerPath in
# appsettings.json, which are ContentRootPath-relative (see runtime stage).
WORKDIR /models/bge-small-en-v1.5

# ── Model + vocabulary download (single layer so they travel together) ──
#
# URLs are pinned to specific HuggingFace commit SHAs, not `main`. An
# upstream retag of `main` could change tokenization or output width with
# no signal at all; the pin plus the sha256 check turns that into a build
# failure instead of a silent embedding-quality regression.
#
# Xenova/bge-small-en-v1.5 @ ea104dacec62c0de699686887e3f920caeb4f3e3
#   onnx/model.onnx — fp32 graph, last_hidden_state output, 133,093,490 bytes
# BAAI/bge-small-en-v1.5 @ 5c38ec7c405ec4b44b94cc5a9bb96e735b38267a
#   vocab.txt — BERT WordPiece, 231,508 bytes
#
# To bump: pick the new commit SHA from the HF repo's commit history, then
# read the new sha256 straight off the HF API without downloading —
#   curl -sS https://huggingface.co/api/models/<repo>/tree/<sha>/<dir>
# For LFS-backed files the `lfs.oid` field IS the sha256. Non-LFS files
# (vocab.txt) report only a git blob SHA-1, so those must be downloaded
# and hashed locally with `sha256sum`.
ARG MODEL_REVISION=ea104dacec62c0de699686887e3f920caeb4f3e3
ARG VOCAB_REVISION=5c38ec7c405ec4b44b94cc5a9bb96e735b38267a
ARG MODEL_SHA256=828e1496d7fabb79cfa4dcd84fa38625c0d3d21da474a00f08db0f559940cf35
ARG VOCAB_SHA256=07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3

RUN curl -fSL \
        "https://huggingface.co/Xenova/bge-small-en-v1.5/resolve/${MODEL_REVISION}/onnx/model.onnx" \
        -o model.onnx \
    && curl -fSL \
        "https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/${VOCAB_REVISION}/vocab.txt" \
        -o vocab.txt \
    && echo "${MODEL_SHA256}  model.onnx" | sha256sum -c - \
    && echo "${VOCAB_SHA256}  vocab.txt"  | sha256sum -c -

# -- Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (layer caching)
COPY OmniSift.sln ./
COPY global.json ./
COPY src/OmniSift.Api/OmniSift.Api.csproj src/OmniSift.Api/
COPY src/OmniSift.Shared/OmniSift.Shared.csproj src/OmniSift.Shared/

# Restore dependencies
RUN dotnet restore src/OmniSift.Api/OmniSift.Api.csproj

# Copy all source code
COPY src/ src/

# Build and publish (single step — avoids stale asset mismatch from --no-restore)
RUN dotnet publish src/OmniSift.Api/OmniSift.Api.csproj -c Release -o /app/publish

# -- Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user for security (Debian-based aspnet image)
RUN groupadd --system appgroup && useradd --system --gid appgroup --no-create-home appuser

COPY --from=build /app/publish .

# Embed the pre-fetched model at /app/models/bge-small-en-v1.5/. The
# EmbeddingOptions defaults are ContentRootPath-relative and OnnxEmbeddingService
# resolves them against IWebHostEnvironment.ContentRootPath:
#   Embedding:ModelPath     = models/bge-small-en-v1.5/model.onnx
#   Embedding:TokenizerPath = models/bge-small-en-v1.5/vocab.txt
# ContentRootPath for the published app is /app, so the resolved absolute paths
# are /app/models/bge-small-en-v1.5/model.onnx and …/vocab.txt.
# Files land root-owned and world-readable, which is what `appuser` needs.
COPY --from=model-fetch /models /app/models

# Set environment
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Pin the ONNX provider in the image. appsettings.json already sets this;
# repeating it here means an operator inspecting `docker inspect` sees the
# real provider without reading the published appsettings.
#
# WARNING: do NOT override with Embedding__Provider=OpenAi without also
# migrating the pgvector column from vector(384) to vector(3072). The column
# is currently vector(384), matching bge-small-en-v1.5. OpenAI
# text-embedding-3-large emits 3072-dimensional vectors. Program.cs refuses to
# start on that mismatch rather than letting the first insert fail.
ENV Embedding__Provider=Onnx
ENV Embedding__ModelPath=models/bge-small-en-v1.5/model.onnx
ENV Embedding__TokenizerPath=models/bge-small-en-v1.5/vocab.txt

EXPOSE 8080

USER appuser

ENTRYPOINT ["dotnet", "OmniSift.Api.dll"]
