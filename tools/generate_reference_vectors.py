#!/usr/bin/env python3
"""
Generate reference embedding vectors for OnnxEmbeddingParityTests.

Requirements (Python 3.10–3.12, NOT 3.14 — no torch wheels yet):
    pip install sentence-transformers torch

Usage:
    python tools/generate_reference_vectors.py \
        > tests/Lindrake.UnitTests/Services/Fixtures/bge_small_en_v1_5_reference_vectors.json

The output is a JSON array where each element has:
    "text":   the raw document text (no query instruction prefix)
    "vector": 384-dimensional L2-normalized float array

These are *document* embeddings (no "Represent this sentence..." prefix).
The C# parity test calls GenerateEmbeddingsAsync (the document path) and
compares cosine similarity >= 0.999 to catch tokenizer/pooling regressions.
"""

import json
import sys
from sentence_transformers import SentenceTransformer

TEXTS = [
    "How do I reset my forgotten password?",
    "The mitochondria is the powerhouse of the cell.",
    "Annual shareholder meeting minutes for fiscal year 2025.",
    "Follow these steps to recover access if you forgot your login credentials.",
    "OmniSift is a multi-tenant AI research assistant with document ingestion.",
]

MODEL_NAME = "BAAI/bge-small-en-v1.5"


def main() -> None:
    model = SentenceTransformer(MODEL_NAME)
    # encode without query instruction (document path; normalize_embeddings=True for L2-norm)
    embeddings = model.encode(TEXTS, normalize_embeddings=True, show_progress_bar=False)

    result = [
        {"text": text, "vector": emb.tolist()}
        for text, emb in zip(TEXTS, embeddings)
    ]
    json.dump(result, sys.stdout, indent=2)
    print()  # trailing newline


if __name__ == "__main__":
    main()
