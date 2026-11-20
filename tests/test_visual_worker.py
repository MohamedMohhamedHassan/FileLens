import base64
import io
import json
import os
import subprocess
import sys
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
PYTHON = os.path.join(ROOT, "src", "ai-worker", ".venv", "Scripts", "python.exe")
WORKER = os.path.join(ROOT, "src", "ai-worker", "worker.py")

def main() -> int:
    image = Image.new("RGB", (64, 48), (220, 40, 40))
    buf = io.BytesIO(); image.save(buf, "PNG")
    proc = subprocess.Popen([PYTHON, WORKER], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
    payload = {"command": "image_embedding", "id": "visual-1", "image_base64": base64.b64encode(buf.getvalue()).decode()}
    proc.stdin.write(json.dumps(payload) + "\n"); proc.stdin.flush()
    response = json.loads(proc.stdout.readline())
    result = response.get("result", {})
    assert response.get("status") == "success"
    assert result.get("model_name") == "FileLens-VisualFeature-CPU"
    assert len(result.get("vector", [])) == 104
    assert result.get("width") == 64 and result.get("height") == 48
    proc.stdin.write(json.dumps({"command": "shutdown", "id": "visual-2"}) + "\n"); proc.stdin.flush(); proc.wait(timeout=10)
    print("visual worker embedding: PASS (104 dimensions, local CPU descriptor)")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
