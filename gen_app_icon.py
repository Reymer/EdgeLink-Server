from PIL import Image, ImageDraw
import math

def make_icon(size=1024):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # 背景漸層
    r = int(size * 0.12)
    pri      = (79,  70, 229)
    pri_dark = (55,  48, 163)
    for y in range(size):
        t  = y / size
        rc = tuple(int(pri[i] * (1-t) + pri_dark[i] * t) for i in range(3))
        d.line([(0, y), (size, y)], fill=rc + (255,))
    bg_mask = Image.new('L', (size, size), 0)
    md = ImageDraw.Draw(bg_mask)
    md.rounded_rectangle([0, 0, size-1, size-1], radius=r, fill=255)
    img.putalpha(bg_mask)
    d = ImageDraw.Draw(img)

    cx = cy   = size // 2
    line_len  = int(size * 0.30)   # 中心到小節點距離
    center_r  = int(size * 0.10)   # 中央節點半徑
    node_r    = int(size * 0.055)  # 小節點半徑
    lw        = max(4, int(size * 0.020))  # 線寬

    white     = (255, 255, 255, 255)
    white_dim = (255, 255, 255, 150)

    # 4 個方向節點（上右下左）
    for dx, dy in [(0,-1),(1,0),(0,1),(-1,0)]:
        nx = cx + int(dx * line_len)
        ny = cy + int(dy * line_len)

        # 連線（從中央節點邊緣到小節點邊緣）
        sx = cx + int(dx * (center_r + lw))
        sy = cy + int(dy * (center_r + lw))
        ex = nx - int(dx * (node_r + lw))
        ey = ny - int(dy * (node_r + lw))
        d.line([(sx, sy), (ex, ey)], fill=white_dim, width=lw)

        # 小節點
        d.ellipse([nx-node_r, ny-node_r, nx+node_r, ny+node_r],
                  fill=white)

    # 中央節點：外圈光暈
    halo_r = int(center_r * 1.55)
    d.ellipse([cx-halo_r, cy-halo_r, cx+halo_r, cy+halo_r],
              fill=None, outline=(255,255,255,55), width=max(3, lw//2))

    # 中央節點：實心圓
    d.ellipse([cx-center_r, cy-center_r, cx+center_r, cy+center_r],
              fill=white)

    # 中央小點（深色，象徵伺服器核心）
    dot_r = int(center_r * 0.35)
    d.ellipse([cx-dot_r, cy-dot_r, cx+dot_r, cy+dot_r],
              fill=pri_dark)

    return img

img = make_icon(1024)
img.save('Assets/EdgeLinkServer_Icon.png')
print('Done: Assets/EdgeLinkServer_Icon.png')
