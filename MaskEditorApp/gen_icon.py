"""
生成 MaskEditor.ico（256x256 → 多尺寸）
圖示來源：IOT-Server/WebUI/index.html .h-logo SVG
  path: M12 2L2 7l10 5 10-5-10-5z M2 17l10 5 10-5 M2 12l10 5 10-5
需要 Pillow：pip install pillow
"""
from PIL import Image, ImageDraw


def make_icon(size):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    r = int(size * 0.18)
    pri = (79, 70, 229)
    pri_dark = (67, 56, 202)

    # 背景漸層
    for y in range(size):
        t = y / size
        rc = tuple(int(pri[i] * (1 - t) + pri_dark[i] * t) for i in range(3))
        d.line([(0, y), (size, y)], fill=rc + (255,))

    bg_mask = Image.new('L', (size, size), 0)
    md = ImageDraw.Draw(bg_mask)
    md.rounded_rectangle([0, 0, size - 1, size - 1], radius=r, fill=255)
    img.putalpha(bg_mask)

    d = ImageDraw.Draw(img)

    # SVG viewBox 0 0 24 24 → 縮放至 icon，加 padding
    pad   = size * 0.16
    scale = (size - 2 * pad) / 24

    def pt(x, y):
        return (pad + x * scale, pad + y * scale)

    white = (255, 255, 255, 255)
    lw    = max(1, round(size / 24 * 2.5))   # 原始 stroke-width 2.5

    # 頂層菱形 (closed): M12 2 L2 7 L12 12 L22 7 Z
    d.line([pt(12, 2), pt(2, 7), pt(12, 12), pt(22, 7), pt(12, 2)],
           fill=white, width=lw)
    # 中層底邊: M2 12 L12 17 L22 12
    d.line([pt(2, 12), pt(12, 17), pt(22, 12)], fill=white, width=lw)
    # 底層底邊: M2 17 L12 22 L22 17
    d.line([pt(2, 17), pt(12, 22), pt(22, 17)], fill=white, width=lw)

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
