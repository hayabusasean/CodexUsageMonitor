"""將已保存的桌面擷取影格轉成 GIF；本腳本不擷取桌面，也不驗證錄影來源。"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path
from typing import Any

from PIL import Image


def within(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def number(value: Any) -> float | None:
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        value = float(value)
        return value if math.isfinite(value) else None
    return None


def elapsed_ms(row: dict[str, Any], meta: dict[str, Any]) -> float | None:
    for key in ("elapsed_ms", "capture_elapsed_ms"):
        if (value := number(row.get(key))) is not None:
            return value
    ticks = number(row.get("qpc"))
    frequency = number(meta.get("qpc_frequency", meta.get("frequency")))
    return ticks * 1000 / frequency if ticks is not None and frequency and frequency > 0 else None


def frame_rows(meta: dict[str, Any], clip: Path) -> list[dict[str, Any]]:
    rows = meta.get("frames")
    if isinstance(rows, list) and rows:
        result: list[dict[str, Any]] = []
        for row in rows:
            if isinstance(row, str):
                result.append({"file": row})
            elif isinstance(row, dict):
                result.append(row)
            else:
                raise ValueError("frames 內含無法辨識的項目")
        return result
    return [{"file": path.name} for path in sorted(clip.glob("frame*.png"))]


def encode_clip(meta_path: Path, source_root: Path, output_root: Path, max_width: int | None) -> dict[str, Any]:
    meta_path = meta_path.resolve()
    if not within(meta_path, source_root):
        raise ValueError("clip.json 路徑超出指定根資料夾")
    meta = json.loads(meta_path.read_text(encoding="utf-8-sig"))
    if not isinstance(meta, dict):
        raise ValueError("clip.json 必須是 JSON 物件")
    if meta.get("guard_passed") is False or meta.get("safety_passed") is False or meta.get("aborted") is True:
        raise ValueError("此段錄影已標記防護未通過或中止，不輸出 GIF")
    clip = meta_path.parent
    rows = frame_rows(meta, clip)
    if not rows:
        raise ValueError("沒有可編碼的影格")
    times = [elapsed_ms(row, meta) for row in rows]
    durations = []
    fallback_intervals = 0
    for index, start in enumerate(times):
        finish = times[index + 1] if index + 1 < len(times) else None
        duration = finish - start if start is not None and finish is not None else 100.0
        if duration <= 0:
            raise ValueError("影格時間必須嚴格遞增")
        if index + 1 < len(times) and (start is None or finish is None):
            fallback_intervals += 1
        # GIF 的時間單位為 10 ms；最後一幀固定保留 100 ms。
        durations.append(max(10, int(math.floor(duration / 10 + 0.5)) * 10))

    pictures: list[Image.Image] = []
    source_dimensions: tuple[int, int] | None = None
    input_frames: list[dict[str, Any]] = []
    try:
        for row in rows:
            file_name = row.get("file", row.get("filename"))
            if not isinstance(file_name, str) or not file_name:
                raise ValueError("影格沒有 file 或 filename")
            image_path = (clip / file_name).resolve()
            if not within(image_path, source_root) or not within(image_path, clip):
                raise ValueError("影格路徑超出指定的擷取資料夾")
            if image_path.suffix.lower() != ".png":
                raise ValueError("只接受 PNG 影格")
            with Image.open(image_path) as source:
                if source_dimensions is None:
                    source_dimensions = source.size
                elif source.size != source_dimensions:
                    raise ValueError("同一段錄影的影格尺寸不一致")
                picture = source.convert("RGB")
            if max_width is not None and picture.width > max_width:
                resized = picture.resize(
                    (max_width, max(1, round(picture.height * max_width / picture.width))),
                    Image.Resampling.LANCZOS,
                )
                picture.close()
                picture = resized
            pictures.append(picture)
            input_frames.append({"file": image_path.relative_to(source_root).as_posix(), "sha256": sha256(image_path)})

        relative = clip.relative_to(source_root)
        destination = output_root / relative / "recording.gif"
        destination.parent.mkdir(parents=True, exist_ok=True)
        temporary = destination.with_suffix(".gif.tmp")
        try:
            pictures[0].save(
                temporary,
                format="GIF",
                save_all=True,
                append_images=pictures[1:],
                duration=durations,
                loop=0,
                disposal=2,
                optimize=False,
            )
            temporary.replace(destination)
        finally:
            if temporary.exists():
                temporary.unlink()
        with Image.open(destination) as encoded:
            encoded_frames = encoded.n_frames
            encoded_duration = 0
            for index in range(encoded_frames):
                encoded.seek(index)
                encoded_duration += int(encoded.info.get("duration", 0))
            encoded_dimensions = encoded.size
        return {
            "clip": relative.as_posix(),
            "result": "PASS",
            "capture_method_declared": meta.get("capture_method", meta.get("source_method", "UNKNOWN")),
            "inputFrames": len(rows),
            "encodedFrames": encoded_frames,
            "source_dimensions": list(source_dimensions or (0, 0)),
            "gif_dimensions": list(encoded_dimensions),
            "resized": source_dimensions != encoded_dimensions,
            "requested_max_width": max_width,
            "requested_duration_ms": sum(durations),
            "encoded_duration_ms": encoded_duration,
            "missing_time_intervals_using_100ms": fallback_intervals,
            "last_frame_duration_ms": 100,
            "gif": destination.relative_to(source_root).as_posix(),
            "sha256": sha256(destination),
            "input_frames": input_frames,
            "note_zh_tw": "僅編碼既有影格；GIF 採 256 色並可能合併完全相同的連續影格，原始 PNG 與時間戳仍保留。擷取來源及視窗安全防護以 clip.json 為準。",
        }
    finally:
        for picture in pictures:
            picture.close()


def main() -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="將指定資料夾內的桌面錄影 PNG 影格編碼成 GIF。")
    parser.add_argument("input_root", type=Path, help="包含各段 clip.json 與 PNG 的根資料夾")
    parser.add_argument("output_root", type=Path, nargs="?", help="輸出資料夾，須位於 input_root 內；預設為 input_root/gif")
    parser.add_argument("--max-width", type=int, default=None, help="選用的 GIF 最大寬度；未指定時維持原始尺寸")
    args = parser.parse_args()
    source_root = args.input_root.resolve()
    output_root = args.output_root.resolve() if args.output_root else source_root / "gif"
    if not source_root.is_dir():
        parser.error("找不到輸入資料夾")
    if not within(output_root, source_root) or output_root == source_root:
        parser.error("輸出資料夾必須是輸入根資料夾內的子資料夾")
    if args.max_width is not None and args.max_width <= 0:
        parser.error("--max-width 必須大於 0")
    clips = [path for path in sorted(source_root.rglob("clip.json")) if not within(path, output_root)]
    if not clips:
        parser.error("找不到 clip.json")
    output_root.mkdir(parents=True, exist_ok=True)
    results = []
    for meta_path in clips:
        try:
            results.append(encode_clip(meta_path, source_root, output_root, args.max_width))
        except Exception as error:
            results.append({"clip": meta_path.parent.relative_to(source_root).as_posix(), "result": "FAIL", "error_zh_tw": str(error)})
    report = {
        "schema": "codex-ui-recording-gif.v1",
        "encoding_only": True,
        "capture_method_verified_by_encoder": False,
        "input_root": str(source_root),
        "output_root": str(output_root),
        "results": results,
    }
    report_path = output_root / "encoding-results.json"
    temporary = report_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(report_path)
    failures = sum(row["result"] != "PASS" for row in results)
    print(f"已編碼 {len(results) - failures}/{len(results)} 段錄影；結果：{report_path}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
