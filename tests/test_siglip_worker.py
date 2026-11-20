"""Phase 4B real SigLIP2 visual-worker IPC verification."""
from __future__ import annotations

import base64
import io
import json
import os
import subprocess
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
PYTHON = Path(r"D:\FileLens-AI\python\venv\Scripts\python.exe")
WORKER = ROOT / "src" / "ai-worker" / "worker.py"
MODEL = Path(r"D:\FileLens-AI\models\siglip2-base-patch16-224")


def main() -> int:
    env = os.environ.copy()
    env.update({
        "FILELENS_WORKER_ROLE": "visual",
        "FILELENS_SIGLIP_MODEL": str(MODEL),
        "HF_HOME": r"D:\FileLens-AI\cache\huggingface",
        "HF_HUB_CACHE": r"D:\FileLens-AI\cache\huggingface\hub",
        "TRANSFORMERS_OFFLINE": "1",
        "HF_HUB_OFFLINE": "1",
    })
    proc = subprocess.Popen([str(PYTHON), str(WORKER)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", env=env)

    def send(payload: dict) -> dict:
        assert proc.stdin and proc.stdout
        proc.stdin.write(json.dumps(payload) + "\n")
        proc.stdin.flush()
        line = proc.stdout.readline()
        assert line, proc.stderr.read() if proc.stderr else "worker exited"
        response = json.loads(line)
        assert response.get("status") == "success", response
        return response

    image = Image.new("RGB", (160, 100), (220, 30, 30))
    buf = io.BytesIO()
    image.save(buf, "PNG")
    encoded = base64.b64encode(buf.getvalue()).decode()
    ping = send({"command": "ping", "id": "siglip-ping"})
    health = send({"command": "health", "id": "siglip-health"})
    result = send({"command": "embed_image", "id": "siglip-embed", "image_base64": encoded})["result"]
    vector = result["vector"]
    norm = sum(value * value for value in vector) ** 0.5
    assert ping["role"] == "visual"
    assert health["visual_provider"] == "SigLIP2VisualEmbeddingProvider"
    assert result["model_name"] == "google/siglip2-base-patch16-224"
    assert len(vector) == 768
    assert abs(norm - 1.0) < 0.001
    send({"command": "shutdown", "id": "siglip-shutdown"})
    proc.wait(timeout=10)
    assert proc.returncode == 0
    print("SigLIP2 visual worker: PASS (local-only IPC, 768 dimensions, normalized CPU vector)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
