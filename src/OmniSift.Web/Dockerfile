# ============================================================
# OmniSift.Web — Multi-stage Docker Build
# Stage 1: Build Blazor WASM with .NET SDK
# Stage 2: Serve with nginx
# ============================================================

# -- Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (layer caching)
COPY OmniSift.sln ./
COPY global.json ./
COPY src/OmniSift.Web/OmniSift.Web.csproj src/OmniSift.Web/
COPY src/OmniSift.Shared/OmniSift.Shared.csproj src/OmniSift.Shared/

# Restore dependencies
RUN dotnet restore src/OmniSift.Web/OmniSift.Web.csproj

# Copy all source code
COPY src/ src/

# Build and publish (single step — avoids stale asset mismatch from --no-restore)
RUN dotnet publish src/OmniSift.Web/OmniSift.Web.csproj -c Release -o /app/publish

# -- Runtime Stage (nginx)
FROM nginx:alpine AS runtime

# Copy the published Blazor WASM output to nginx
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html/app
COPY landing/ /usr/share/nginx/html/

# Copy custom nginx config for SPA routing
COPY src/OmniSift.Web/nginx.conf /etc/nginx/conf.d/default.conf

# Renders app/appsettings.json from API_BASE_URL at container start (the nginx
# image's entrypoint runs /docker-entrypoint.d/*.sh before nginx), so one image
# serves any environment. Empty API_BASE_URL = same-origin api/... requests.
COPY src/OmniSift.Web/docker-entrypoint.d/ /docker-entrypoint.d/
RUN sed -i 's/\r$//' /docker-entrypoint.d/40-render-appsettings.sh \
    && chmod +x /docker-entrypoint.d/40-render-appsettings.sh

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]