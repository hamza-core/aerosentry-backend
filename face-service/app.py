"""
AeroSentry face service.

A small, stateless FastAPI service around InsightFace's `buffalo_l` model (the same model and settings as the
learning scripts in Documents/CV/learn: SCRFD face detector + ArcFace recogniser, CPU, 512-number embeddings).

What it does: given a picture, it finds the faces and returns, for each one, its box and its embedding
(512 numbers, scaled to length 1). That is all. It stores nothing and knows nobody: the backend keeps the people,
their photos and their embeddings in the database, and decides who matches whom (cosine similarity = a dot product).

Runs fully offline: the model files are read from ~/.insightface/models/buffalo_l.

Run (from this folder, in the conda environment that has insightface, opencv and onnxruntime):
    python -m uvicorn app:app --port 8020
"""

import logging
import os
import threading
from contextlib import asynccontextmanager

import cv2
import numpy as np
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from insightface.app import FaceAnalysis

logging.basicConfig(level=logging.INFO, format="%(asctime)s  %(levelname)-7s %(message)s", datefmt="%H:%M:%S")
log = logging.getLogger("face-service")

MODEL_NAME = os.environ.get("FACE_MODEL", "buffalo_l")
DEFAULT_DET_SIZE = 640              # same as the learning scripts. A bigger value finds smaller faces but is slower.
MAX_IMAGE_BYTES = 15 * 1024 * 1024

state = {"app": None, "det_size": DEFAULT_DET_SIZE}
lock = threading.Lock()             # the detector's input size is shared state: one request at a time (about 70 ms each)


@asynccontextmanager
async def lifespan(_: FastAPI):
    log.info("Loading %s (CPU, offline)...", MODEL_NAME)
    # Only the two modules that are needed: face detection and face recognition (the age/gender and landmark models are skipped).
    face_app = FaceAnalysis(name=MODEL_NAME, providers=["CPUExecutionProvider"], allowed_modules=["detection", "recognition"])
    face_app.prepare(ctx_id=-1, det_size=(DEFAULT_DET_SIZE, DEFAULT_DET_SIZE))
    state["app"] = face_app
    log.info("Face service ready.")
    yield


app = FastAPI(title="AeroSentry face service", version="0.1.0", lifespan=lifespan)


@app.get("/api/health")
def health():
    return {"status": "healthy" if state["app"] is not None else "loading", "model": MODEL_NAME,
            "default_det_size": DEFAULT_DET_SIZE, "provider": "CPUExecutionProvider"}


@app.post("/api/faces/analyze")
def analyze(image: UploadFile = File(..., description="A picture (JPEG or PNG)"),
            det_size: int = Form(DEFAULT_DET_SIZE, description="Detector input size, 320 to 1920. Bigger finds smaller faces, slower.")):
    if state["app"] is None:
        raise HTTPException(503, "The face model is still loading")
    if not 320 <= det_size <= 1920:
        raise HTTPException(422, "det_size must be between 320 and 1920")

    data = image.file.read(MAX_IMAGE_BYTES + 1)
    if len(data) > MAX_IMAGE_BYTES:
        raise HTTPException(422, "The picture is larger than 15 MB")
    frame = cv2.imdecode(np.frombuffer(data, dtype=np.uint8), cv2.IMREAD_COLOR)
    if frame is None:
        raise HTTPException(422, "The upload is not a picture the service can read")

    with lock:
        if det_size != state["det_size"]:
            state["app"].det_model.input_size = (det_size, det_size)
            state["det_size"] = det_size
        faces = state["app"].get(frame)

    height, width = frame.shape[:2]
    result = []
    for face in sorted(faces, key=lambda f: -(f.bbox[2] - f.bbox[0]) * (f.bbox[3] - f.bbox[1])):   # biggest face first
        embedding = face.embedding / np.linalg.norm(face.embedding)
        result.append({
            "box": [int(v) for v in face.bbox],                      # [x1, y1, x2, y2] in pixels of the picture
            "det_score": round(float(face.det_score), 4),            # how sure the detector is that this is a face (0 to 1)
            "embedding": [round(float(v), 6) for v in embedding],    # 512 numbers, length 1
        })
    return {"image_width": width, "image_height": height, "faces": result}
