"""
生成 MaskEditor.ico（256x256 → 多尺寸）
需要 Pillow：pip install pillow
"""
from PIL import Image, ImageDraw
import math

def make_icon(size):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    r = int(size * 0.18)          # 圓角半徑
    pri = (79, 70, 229)           # #4f46e5
    pri_dark = (67, 56, 202)      # #4338ca

    # 背景圓角矩形（漸層模擬：上半 pri，下半 pri_dark）
    for y in range(size):
        t = y / size
        rc = tuple(int(pri[i] * (1 - t) + pri_dark[i] * t) for i in range(3))
        d.line([(0, y), (size, y)], fill=rc + (255,))
    # 套用圓角遮罩
    mask = Image.new('L', (size, size), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([0, 0, size - 1, size - 1], radius=r, fill=255)
    img.putalpha(mask)

    # 重畫前景
    d = ImageDraw.Draw(img)

    w = size
    pad = int(w * 0.18)
    white = (255, 255, 255, 230)
    lw = max(2, int(w * 0.055))

    # 左：垂直線（資料進入）
    lx = pad
    d.line([(lx, pad), (lx, w - pad)], fill=white, width=lw)

    # 右：垂直線（資料輸出）
    rx = w - pad
    d.line([(rx, pad), (rx, w - pad)], fill=white, width=lw)

    # 中央：三條水平過濾線（象徵遮罩/濾波）
    cx = w // 2
    for i, ratio in enumerate([0.3, 0.5, 0.7]):
        y = int(w * ratio)
        half = int(w * (0.14 + i * 0.03))
        d.line([(cx - half, y), (cx + half, y)], fill=white, width=lw)

    # 左線 → 中央連接（三條水平橫線的左端）
    for i, ratio in enumerate([0.3, 0.5, 0.7]):
        y = int(w * ratio)
        half = int(w * (0.14 + i * 0.03))
        d.line([(lx, y), (cx - half, y)], fill=(255, 255, 255, 80), width=max(1, lw // 2))
        d.line([(cx + half, y), (rx, y)], fill=(255, 255, 255, 80), width=max(1, lw // 2))

    return img


def main():
    sizes = [256, 128, 64, 48, 32, 16]
    frames = [make_icon(s) for s in sizes]
    frames[0].save(
        'MaskEditor.ico',
        format='ICO',
        sizes=[(s, s) for s in sizes],
        append_images=frames[1:]
    )
    print('MaskEditor.ico 已生成')


if __name__ == '__main__':
    main()
