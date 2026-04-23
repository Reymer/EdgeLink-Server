#!/usr/bin/env python3
"""
生成 EdgeLink Server 使用說明書 PDF（繁中 + 英文）
Generate EdgeLink Server User Manual PDF (zh-TW + en-US)

Usage:  python gen_manual.py
Output: docs/EdgeLinkServer_Manual_zh-TW.pdf
        docs/EdgeLinkServer_Manual_en-US.pdf

Requires: pip install reportlab
"""

import os, sys, subprocess

def _install(pkg):
    subprocess.check_call([sys.executable, '-m', 'pip', 'install', pkg],
                          stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

try:
    from reportlab.lib.pagesizes import A4
    from reportlab.lib.styles import ParagraphStyle
    from reportlab.lib.units import cm
    from reportlab.lib.colors import HexColor, white, black
    from reportlab.lib.enums import TA_CENTER, TA_LEFT, TA_JUSTIFY
    from reportlab.platypus import (SimpleDocTemplate, Paragraph, Spacer, Table,
                                     TableStyle, PageBreak, HRFlowable, KeepTogether)
    from reportlab.pdfbase import pdfmetrics
    from reportlab.pdfbase.ttfonts import TTFont
    from reportlab.pdfgen import canvas as pdfcanvas
except ImportError:
    print('Installing reportlab …')
    _install('reportlab')
    from reportlab.lib.pagesizes import A4
    from reportlab.lib.styles import ParagraphStyle
    from reportlab.lib.units import cm
    from reportlab.lib.colors import HexColor, white, black
    from reportlab.lib.enums import TA_CENTER, TA_LEFT, TA_JUSTIFY
    from reportlab.platypus import (SimpleDocTemplate, Paragraph, Spacer, Table,
                                     TableStyle, PageBreak, HRFlowable, KeepTogether)
    from reportlab.pdfbase import pdfmetrics
    from reportlab.pdfbase.ttfonts import TTFont
    from reportlab.pdfgen import canvas as pdfcanvas

# ── Palette ───────────────────────────────────────────────────
PRI     = HexColor('#4f46e5')
PRI_D   = HexColor('#4338ca')
PRI_LT  = HexColor('#eef2ff')
CODE_BG = HexColor('#f1f5f9')
BORDER  = HexColor('#cbd5e1')
TXT     = HexColor('#0f172a')
TXT2    = HexColor('#475569')
TXT3    = HexColor('#94a3b8')
OK      = HexColor('#10b981')
WARN    = HexColor('#f59e0b')
TH_BG   = HexColor('#f8fafc')

W, H = A4  # 595.27 x 841.89 pt

# ── Font helpers ──────────────────────────────────────────────
BODY = BOLD = MONO = ''

def _setup_fonts(cjk: bool):
    global BODY, BOLD, MONO
    MONO = 'Courier'
    if not cjk:
        BODY = 'Helvetica'
        BOLD = 'Helvetica-Bold'
        return True

    candidates = [
        ('C:/Windows/Fonts/msjh.ttc',   'C:/Windows/Fonts/msjhbd.ttc'),
        ('C:/Windows/Fonts/kaiu.ttf',    None),
        ('C:/Windows/Fonts/mingliu.ttc', None),
    ]
    for reg_path, bold_path in candidates:
        if not os.path.exists(reg_path):
            continue
        try:
            pdfmetrics.registerFont(TTFont('CJK', reg_path))
            BODY = 'CJK'
            if bold_path and os.path.exists(bold_path):
                pdfmetrics.registerFont(TTFont('CJK-Bold', bold_path))
                BOLD = 'CJK-Bold'
            else:
                BOLD = 'CJK'
            print(f'  Font: {reg_path}')
            return True
        except Exception as e:
            continue

    print('  Warning: CJK font not found – using Helvetica (Chinese may not render)')
    BODY = 'Helvetica'
    BOLD = 'Helvetica-Bold'
    return False

# ── Styles ────────────────────────────────────────────────────
def _styles():
    def S(name, **kw):
        kw.setdefault('fontName', BODY)
        kw.setdefault('textColor', TXT)
        kw.setdefault('leading', 16)
        return ParagraphStyle(name=name, **kw)
    return {
        'h1':    S('h1',  fontName=BOLD, fontSize=20, textColor=PRI,
                   spaceAfter=6, spaceBefore=18, leading=26),
        'h2':    S('h2',  fontName=BOLD, fontSize=14, textColor=PRI,
                   spaceAfter=4, spaceBefore=14, leading=20),
        'h3':    S('h3',  fontName=BOLD, fontSize=11, textColor=TXT,
                   spaceAfter=3, spaceBefore=10, leading=16),
        'body':  S('body', fontSize=9.5, textColor=TXT2,
                   spaceAfter=5, leading=15, alignment=TA_JUSTIFY),
        'li':    S('li',   fontSize=9.5, textColor=TXT2,
                   spaceAfter=3, leading=14, leftIndent=14),
        'note':  S('note', fontSize=8.5, textColor=TXT3,
                   spaceAfter=4, leading=13, leftIndent=10),
        'code':  ParagraphStyle(name='code', fontName=MONO, fontSize=8.5,
                                textColor=TXT, leading=13, leftIndent=10,
                                rightIndent=10, backColor=CODE_BG,
                                borderPad=6, spaceAfter=8, spaceBefore=4),
        'th':    S('th',  fontName=BOLD, fontSize=8.5, textColor=TXT,
                   alignment=TA_CENTER, leading=12),
        'td':    S('td',  fontSize=8.5,  textColor=TXT2, leading=12,
                   alignment=TA_LEFT),
        'cover_title': S('cover_title', fontName=BOLD, fontSize=32,
                          textColor=white, leading=38, alignment=TA_CENTER),
        'cover_sub':   S('cover_sub',   fontName=BODY, fontSize=13,
                          textColor=HexColor('#c7d2fe'), leading=18,
                          alignment=TA_CENTER),
        'cover_ver':   S('cover_ver',   fontName=BODY, fontSize=10,
                          textColor=HexColor('#a5b4fc'), leading=14,
                          alignment=TA_CENTER),
    }

# ── Helpers ───────────────────────────────────────────────────
def HR(): return HRFlowable(width='100%', thickness=0.5,
                             color=BORDER, spaceAfter=8, spaceBefore=8)

def SP(h=6): return Spacer(1, h)

def tbl(data, col_widths, S):
    """Build a styled table from list-of-lists."""
    rows = []
    for i, row in enumerate(data):
        rows.append([Paragraph(str(c), S['th'] if i == 0 else S['td']) for c in row])
    t = Table(rows, colWidths=col_widths, repeatRows=1)
    t.setStyle(TableStyle([
        ('BACKGROUND', (0,0), (-1,0), TH_BG),
        ('ROWBACKGROUNDS', (0,1), (-1,-1), [white, PRI_LT]),
        ('GRID',       (0,0), (-1,-1), 0.4, BORDER),
        ('TOPPADDING',  (0,0), (-1,-1), 4),
        ('BOTTOMPADDING',(0,0), (-1,-1), 4),
        ('LEFTPADDING', (0,0), (-1,-1), 6),
        ('RIGHTPADDING',(0,0), (-1,-1), 6),
        ('VALIGN',     (0,0), (-1,-1), 'MIDDLE'),
    ]))
    return t

def code_block(lines, S):
    text = '<br/>'.join(lines)
    return Paragraph(text, S['code'])

# ── Page template (header + footer) ──────────────────────────
class _DocTemplate(SimpleDocTemplate):
    def __init__(self, *a, title='', company='', **kw):
        super().__init__(*a, **kw)
        self._doc_title   = title
        self._doc_company = company
        self._page_num    = 0

    def handle_pageEnd(self):
        self._page_num += 1
        c = self.canv
        c.saveState()
        # Footer bar
        c.setFillColor(TH_BG)
        c.rect(0, 0, W, 1.2*cm, fill=1, stroke=0)
        c.setStrokeColor(BORDER)
        c.setLineWidth(0.5)
        c.line(1.5*cm, 1.2*cm, W-1.5*cm, 1.2*cm)
        # Footer text
        c.setFont(BODY or 'Helvetica', 7.5)
        c.setFillColor(TXT3)
        c.drawString(1.8*cm, 0.45*cm, self._doc_company)
        c.drawRightString(W-1.8*cm, 0.45*cm, str(self._page_num))
        c.drawCentredString(W/2, 0.45*cm, self._doc_title)
        c.restoreState()
        super().handle_pageEnd()

# ── Cover page ────────────────────────────────────────────────
def _cover(S, title_line1, title_line2, subtitle, version, company, date):
    """Returns a list of flowables for the cover page."""
    story = []

    # Draw coloured cover using a custom flowable
    class _Cover(object):
        def __init__(self): self.width = W; self.height = H
        def wrap(self, *a): return (W, H)
        def drawOn(self, canvas, x, y):
            canvas.saveState()
            # Background gradient (two rectangles)
            canvas.setFillColor(PRI)
            canvas.rect(0, 0, W, H, fill=1, stroke=0)
            canvas.setFillColor(PRI_D)
            canvas.rect(0, 0, W, H*0.45, fill=1, stroke=0)
            # Decorative corner arc
            canvas.setStrokeColor(HexColor('#6366f1'))
            canvas.setLineWidth(60)
            canvas.setFillColor(HexColor('#00000000'))
            canvas.circle(W+80, H+80, 260, fill=0, stroke=1)
            canvas.setLineWidth(30)
            canvas.circle(W+60, H+60, 340, fill=0, stroke=1)
            # Logo layers icon
            cx, cy = W/2, H*0.62
            sc = 2.2
            def pt(px, py): return (cx + (px-12)*sc*3, cy + (py-12)*sc*3)
            canvas.setStrokeColor(white)
            canvas.setLineWidth(3.5)
            # top diamond
            canvas.lines([pt(12,2)+pt(2,7), pt(2,7)+pt(12,12),
                           pt(12,12)+pt(22,7), pt(22,7)+pt(12,2)])
            # middle row
            canvas.line(*pt(2,12), *pt(12,17)); canvas.line(*pt(12,17), *pt(22,12))
            # bottom row
            canvas.line(*pt(2,17), *pt(12,22)); canvas.line(*pt(12,22), *pt(22,17))
            canvas.restoreState()

    from reportlab.platypus.flowables import Flowable
    class CoverBg(Flowable):
        def wrap(self, *a): return (0, 0)
        def draw(self):
            c = self.canv
            c.saveState()
            c.setFillColor(PRI)
            c.rect(-100, -H, W+200, H+200, fill=1, stroke=0)
            c.setFillColor(PRI_D)
            c.rect(-100, -H, W+200, H*0.45, fill=1, stroke=0)
            # decorative circles
            c.setStrokeColor(HexColor('#6366f1'))
            c.setFillColor(white)
            c.setLineWidth(50)
            c.circle(W+60, 160, 280, fill=0, stroke=1)
            c.setLineWidth(25)
            c.circle(W+80, 200, 370, fill=0, stroke=1)
            # layers icon
            cx, cy = W/2, 340
            s = 14
            def pt(px, py): return (cx+(px-12)*s, cy+(py-12)*s)
            c.setStrokeColor(white); c.setLineWidth(4)
            paths = [
                [pt(12,2), pt(2,7), pt(12,12), pt(22,7), pt(12,2)],
                [pt(2,12), pt(12,17), pt(22,12)],
                [pt(2,17), pt(12,22), pt(22,17)],
            ]
            for pts in paths:
                p = c.beginPath()
                p.moveTo(*pts[0])
                for pp in pts[1:]: p.lineTo(*pp)
                c.drawPath(p, stroke=1, fill=0)
            c.restoreState()

    story.append(CoverBg())
    story.append(Spacer(1, H*0.55))
    story.append(Paragraph(title_line1, S['cover_title']))
    if title_line2:
        story.append(Paragraph(title_line2, S['cover_sub']))
    story.append(SP(16))
    story.append(Paragraph(subtitle, S['cover_sub']))
    story.append(SP(12))
    story.append(Paragraph(version, S['cover_ver']))
    story.append(SP(8))
    story.append(Paragraph(f'{company}  ·  {date}', S['cover_ver']))
    story.append(PageBreak())
    return story

# ══════════════════════════════════════════════════════════════
#  CONTENT  ── zh-TW
# ══════════════════════════════════════════════════════════════
def content_zhtw(S):
    P  = lambda t, s='body': Paragraph(t, S[s])
    H1 = lambda t: [SP(4), P(t, 'h1'), HR()]
    H2 = lambda t: [SP(2), P(t, 'h2')]
    H3 = lambda t: [P(t, 'h3')]
    LI = lambda t: P(f'• &nbsp;{t}', 'li')
    N  = lambda t: P(f'※ {t}', 'note')
    CB = lambda *lines: code_block(list(lines), S)
    story = []

    # ── 目錄 ────────────────────────────────────────────────
    story += [P('目  錄', 'h1'), HR()]
    toc_data = [
        ['章節', '標題', '頁'],
        ['1', '產品概述', '3'],
        ['2', '系統需求', '3'],
        ['3', '啟動與連線', '4'],
        ['4', '遮罩管理', '4'],
        ['5', 'Port 管理', '6'],
        ['6', 'EdgeLink SDK', '8'],
        ['7', '常見問題', '9'],
    ]
    story.append(tbl(toc_data, [1.5*cm, 11*cm, 2*cm], S))
    story.append(PageBreak())

    # ── Ch1 產品概述 ─────────────────────────────────────────
    story += H1('1. 產品概述')
    story.append(P(
        'EdgeLink Server 是一套以 Unity 引擎為基礎的工業 IoT 數據中繼伺服器。'
        '它能接收 IoT 設備透過 TCP 或 UDP 協議傳送的原始數據，'
        '透過可自訂的「遮罩」規則進行解析與格式化，'
        '再透過 EdgeLink SDK 轉發給 Unity 應用程式（如 VR 訓練、數位孿生等）即時使用。',
        'body'))
    story.append(SP())
    story += H2('主要功能')
    for item in [
        '多協議支援：TCP Server、TCP Client、UDP',
        '遮罩系統：文字解析與二進位解析，支援自訂輸出格式模板',
        'Web 管理介面：使用瀏覽器管理遮罩定義及 Port 設定',
        '即時 Monitor Log：逐筆查看每個 Port 接收到的原始數據',
        'EdgeLink SDK：.NET Standard 2.0 DLL，可直接嵌入 Unity 專案',
        '多語系介面：繁體中文、English、日本語',
    ]:
        story.append(LI(item))
    story.append(SP())

    story += H2('數據流示意')
    flow_data = [
        ['IoT 設備', '→', 'EdgeLink Server', '→', 'Unity 應用程式'],
        ['傳感器/PLC', 'TCP / UDP', '解析 + 格式化', 'EdgeLink SDK', '遊戲 / VR / 數位孿生'],
    ]
    story.append(tbl(flow_data, [3*cm, 1.5*cm, 4*cm, 3.5*cm, 4*cm], S))
    story.append(SP(10))

    # ── Ch2 系統需求 ─────────────────────────────────────────
    story += H1('2. 系統需求')
    req_data = [
        ['項目', '需求'],
        ['作業系統', 'Windows 10 / 11  64-bit'],
        ['記憶體', '4 GB RAM（建議 8 GB）'],
        ['網路', '支援 TCP/IP 的本地或區域網路'],
        ['瀏覽器（Web UI）', 'Google Chrome、Microsoft Edge、Firefox（最新版）'],
        ['Unity（開發者）', 'Unity 2020.3 LTS 或以上（含 .NET Standard 2.0 支援）'],
    ]
    story.append(tbl(req_data, [5*cm, 10.5*cm], S))
    story.append(SP(10))

    # ── Ch3 啟動與連線 ───────────────────────────────────────
    story += H1('3. 啟動與連線')
    story += H2('3.1 啟動應用程式')
    story.append(LI('執行 EdgeLinkServer.exe（或在 Unity 編輯器中按 Play）'))
    story.append(LI('應用程式啟動後，HTTP 伺服器會自動在 Port 8181 開始監聽'))
    story.append(LI('Unity 主視窗右下角會顯示連線狀態圖示'))
    story.append(SP())
    story += H2('3.2 開啟 Web 管理介面')
    story.append(P('在同一台電腦的瀏覽器輸入以下網址：', 'body'))
    story.append(CB('http://localhost:8181'))
    story.append(LI('左側面板：遮罩列表與新增/匯入功能'))
    story.append(LI('右側上方：遮罩定義編輯器（選擇遮罩後顯示）'))
    story.append(LI('右側下方：Port 狀態表格與 Monitor Log'))
    story.append(N('Web UI 目前僅支援本機連線（localhost）。若需遠端存取，請自行設定網路轉發。'))
    story.append(SP(10))

    # ── Ch4 遮罩管理 ─────────────────────────────────────────
    story += H1('4. 遮罩管理')
    story += H2('4.1 什麼是遮罩？')
    story.append(P(
        '遮罩（Mask）定義了兩件事：'
        '（1）如何解析 IoT 設備傳送的原始字串或二進位數據；'
        '（2）如何將解析結果格式化成輸出字串，供下游應用程式使用。'
        '每個 Port 可指定一個遮罩；預設遮罩 OriginalData 直接透傳原始數據。',
        'body'))
    story.append(SP())

    story += H2('4.2 遮罩列表操作')
    mask_ops = [
        ['操作', '說明'],
        ['點擊遮罩名稱', '開啟右側編輯器'],
        ['新增遮罩', '輸入 Mask ID（必填）與本地化鍵，點擊「新增遮罩」'],
        ['刪除遮罩', '點擊遮罩列表旁的 × 圖示（OriginalData 不可刪）'],
        ['匯入 JSON', '點擊上傳圖示，選擇 SDK JSON 檔案'],
        ['匯出 SDK JSON', '在編輯器右上角點擊「匯出 SDK JSON」'],
    ]
    story.append(tbl(mask_ops, [4.5*cm, 11*cm], S))
    story.append(SP())

    story += H2('4.3 文字模式（Text Mode）')
    story.append(P('適用於 IoT 設備傳送純文字格式的數據（如 NMEA、自訂 KV 格式）。', 'body'))
    story.append(SP(4))
    story += H3('Step 1 — 貼上範例數據')
    story.append(LI('在文字框貼上 IoT 設備實際傳送的範例字串'))
    story.append(LI('例如：ID:1;TEMP:25.3;HUM:60'))
    story.append(LI('設定「欄位分隔符」（Field Delimiter，預設 ;）'))
    story.append(LI('設定「KV 分隔符」（Key-Value Separator，預設 :）'))
    story.append(LI('點擊「自動偵測」可讓系統自動推測分隔符組合'))
    story.append(LI('下方即時顯示解析後的欄位與值'))
    story.append(SP(4))
    story += H3('Step 2 — 定義輸出格式')
    story.append(LI('在輸出格式欄使用 {欄位名稱} 佔位符，例如：ID={ID}&T={TEMP}'))
    story.append(LI('{raw} 代表直接輸出原始字串（不做任何轉換）'))
    story.append(LI('點擊上方藍色欄位標籤，可快速插入佔位符'))
    story.append(SP(4))
    story += H3('Step 3 — 即時預覽')
    story.append(LI('左側顯示輸入字串，右側即時顯示套用格式後的輸出'))
    story.append(LI('若佔位符找不到對應欄位，會顯示橘色警告'))
    story.append(SP())

    story += H2('4.4 二進位模式（Binary Mode）')
    story.append(P('適用於 MCU 傳送原生二進位封包的場景。', 'body'))
    story.append(LI('點擊「+ 新增欄位」，填入名稱、Offset（Byte 起始位置）、Length（位元組數）'))
    story.append(LI('選擇資料型別：'))
    story.append(SP(2))
    dtype_data = [
        ['型別', '大小', '說明'],
        ['uint8',    '1 byte',  '無符號 8 位整數'],
        ['uint16_le','2 bytes', '無符號 16 位整數（小端序）'],
        ['uint16_be','2 bytes', '無符號 16 位整數（大端序）'],
        ['int32_le', '4 bytes', '有符號 32 位整數（小端序）'],
        ['float_le', '4 bytes', 'IEEE 754 單精度浮點數（小端序）'],
        ['hex',      '任意',    '十六進位字串，如 1A2B3C'],
    ]
    story.append(tbl(dtype_data, [2.5*cm, 2*cm, 11*cm], S))
    story.append(N('二進位模式的預覽需連接實際設備測試，Web UI 不提供模擬輸入。'))
    story.append(SP())

    story += H2('4.5 儲存與匯出')
    story.append(LI('點擊右上角「儲存定義」，將設定寫入伺服器（持久化到 Setting/ 資料夾）'))
    story.append(LI('點擊「匯出 SDK JSON」，下載 mask_<ID>.json 供 EdgeLink SDK 使用'))
    story.append(LI('匯出的 JSON 放入 Unity 專案的 Assets/StreamingAssets/ 即可'))
    story.append(SP(10))

    # ── Ch5 Port 管理 ────────────────────────────────────────
    story += H1('5. Port 管理')
    story += H2('5.1 協議說明')
    proto_data = [
        ['協議', '角色', '使用場景'],
        ['TCP Server', '被動監聽', 'IoT 設備主動連入本機；支援多個客戶端同時連線'],
        ['TCP Client', '主動連線', '本機主動連到遠端 IoT 設備或伺服器'],
        ['UDP',        '無連線',   '接收端口收取 UDP 封包；可選擇性轉發到目標 IP'],
    ]
    story.append(tbl(proto_data, [2.5*cm, 2.5*cm, 10.5*cm], S))
    story.append(SP())

    story += H2('5.2 新增 Port')
    story.append(P('點擊右上角「新增 Port」按鈕，依協議填入對應欄位：', 'body'))
    story.append(SP(4))
    field_data = [
        ['欄位', 'TCP Server', 'TCP Client', 'UDP'],
        ['名稱',     '必填',     '必填',         '必填'],
        ['本地端口', '監聽端口', '（自動分配）',  '轉發目標端口'],
        ['遠端端口', '—',        '連線目標端口',  '接收端口'],
        ['目標 IP',  '—',        '必填',          '選填（空=廣播本地）'],
        ['遮罩',     '選填',     '選填',          '選填'],
    ]
    story.append(tbl(field_data, [3*cm, 3*cm, 3*cm, 5.5*cm], S))
    story.append(SP(4))
    story.append(N('TCP Server 的「目標 IP」與「遠端端口」不需填寫。'))
    story.append(N('UDP 若不填「目標 IP」，伺服器會將收到的數據廣播到本地端口。'))
    story.append(SP())

    story += H2('5.3 連線狀態')
    status_data = [
        ['狀態', '說明'],
        ['🟢 連線中', 'TCP：已成功建立連線  |  UDP：成功綁定端口'],
        ['⚪ 未連線', 'TCP：等待連線或連線中斷  |  UDP 通常為無連線狀態'],
    ]
    story.append(tbl(status_data, [3*cm, 12.5*cm], S))
    story.append(SP())

    story += H2('5.4 更換遮罩')
    story.append(LI('在 Port 表格的「遮罩」欄，點擊遮罩名稱旁的下拉選單'))
    story.append(LI('選擇新遮罩後點擊 Apply，伺服器會自動重啟該 Port 的連接器'))
    story.append(N('TCP Server 暫不支援動態切換遮罩。'))
    story.append(SP())

    story += H2('5.5 Monitor Log')
    story.append(LI('點擊 Port 旁的「監控」按鈕，開啟即時 Log 視窗'))
    story.append(LI('每筆收到的原始數據（套用遮罩後的輸出）會即時顯示'))
    story.append(LI('搜尋框支援關鍵字過濾（不區分大小寫）'))
    story.append(LI('「停止自動捲動」按鈕：保持目前捲動位置，方便查看歷史紀錄'))
    story.append(LI('「清除」按鈕：清空目前顯示的 Log（不影響歷史數據）'))
    story.append(LI('同時只能監控一個 Port；點擊其他 Port 的「監控」會自動切換'))
    story.append(SP(10))

    # ── Ch6 EdgeLink SDK ─────────────────────────────────────
    story += H1('6. EdgeLink SDK')
    story += H2('6.1 安裝')
    story.append(LI('將 EdgeLinkSdk.dll 複製到 Unity 專案的 Assets/Plugins/ 資料夾'))
    story.append(LI('將從 Web UI 匯出的 mask_<ID>.json 複製到 Assets/StreamingAssets/'))
    story.append(N('EdgeLinkSdk.dll 無需額外安裝相依套件，僅需單一 DLL 檔案。'))
    story.append(SP())

    story += H2('6.2 快速入門')
    story.append(P('建立 MonoBehaviour，掛載到任一 GameObject：', 'body'))
    story.append(CB(
        'using System.IO;',
        'using EdgeLink;',
        'using UnityEngine;',
        '',
        'public class MyReceiver : MonoBehaviour',
        '{',
        '    EdgeLinkReceiver _receiver;',
        '    MaskParser       _parser;',
        '',
        '    void Start()',
        '    {',
        '        // 1. 載入遮罩定義',
        '        string path = Path.Combine(',
        '            Application.streamingAssetsPath, "sensor.json");',
        '        var mask = MaskDefinition.FromJsonFile(path);',
        '        _parser   = new MaskParser(mask);',
        '',
        '        // 2. 啟動接收器（TCP，監聽 9090 port）',
        '        _receiver = new EdgeLinkReceiver(Protocol.TCP, mask);',
        '        _receiver.OnMessage += OnMessage;',
        '        _receiver.Start(9090);',
        '    }',
        '',
        '    void Update()  => _receiver?.Flush(); // 必須在主執行緒呼叫',
        '    void OnDestroy()=> _receiver?.Dispose();',
        '',
        '    void OnMessage(IotMessage msg)',
        '    {',
        '        var fields = _parser.ParseOutput(msg.Raw);',
        '        if (fields.TryGetValue("TEMP", out string t))',
        '            Debug.Log($"Temperature: {t}");',
        '    }',
        '}',
    ))
    story.append(SP())

    story += H2('6.3 API 參考')
    api_data = [
        ['類別 / 列舉', '說明'],
        ['EdgeLinkReceiver',    '建立 TCP 或 UDP 監聽，接收 IoT Server 轉發的數據'],
        ['MaskDefinition',      '遮罩定義，可從 JSON 字串或檔案載入'],
        ['MaskParser',          '從 IoT Server 輸出字串反向解析出各欄位值'],
        ['IotMessage',          '接收到的訊息，msg.Raw 為完整輸出字串'],
        ['Protocol',            'Protocol.TCP 或 Protocol.UDP 列舉'],
    ]
    story.append(tbl(api_data, [4.5*cm, 11*cm], S))
    story.append(SP(4))
    story += H2('6.4 重要注意事項')
    story.append(LI('EdgeLinkReceiver.Flush() 必須每幀在 Update() 中呼叫，事件才會在主執行緒觸發'))
    story.append(LI('在 IoT Server Web UI 中，Port 的遮罩設定需與 SDK 使用的 JSON 對應'))
    story.append(LI('在 IoT Server 中新增一個 TCP Client，目標 IP 填 Unity 所在機器 IP，Port 填 Start() 傳入的 listenPort'))
    story.append(SP(10))

    # ── Ch7 常見問題 ─────────────────────────────────────────
    story += H1('7. 常見問題')
    faq = [
        ('Web UI 無法開啟（連線失敗）',
         '確認 EdgeLinkServer 應用程式正在執行中（Unity 需處於 Play Mode）。'
         '瀏覽器輸入 http://localhost:8181，狀態列應顯示綠色圓點。'),
        ('Port 狀態一直顯示「未連線」',
         'TCP Client：確認目標設備的 IP 與端口正確，且設備有開啟監聽。\n'
         'TCP Server：等待 IoT 設備主動連入本機 IP 和指定端口。\n'
         'UDP：UDP 為無連線協議，以有無收到數據為準。'),
        ('遮罩重啟後消失',
         '遮罩儲存在 Setting/MaskDefinitions.setting 檔案。'
         '確認應用程式有寫入權限。建議額外匯出 SDK JSON 作為備份。'),
        ('EdgeLink SDK 在 Unity 顯示編譯錯誤',
         '確認 EdgeLinkSdk.dll 已放在 Assets/Plugins/ 資料夾。'
         '無需安裝其他相依套件。重新匯入（Reimport）Assets/Plugins 資料夾後再嘗試。'),
        ('Monitor Log 沒有資料',
         '確認對應的 IoT 設備正在發送數據，且 Port 設定的協議與端口與設備一致。'
         '可用 TCP/UDP 測試工具（如 netcat）手動發送數據驗證連通性。'),
    ]
    for q, a in faq:
        story += H3(f'Q: {q}')
        story.append(P(f'A: {a}', 'body'))
        story.append(SP(4))

    return story


# ══════════════════════════════════════════════════════════════
#  CONTENT  ── en-US
# ══════════════════════════════════════════════════════════════
def content_enus(S):
    P  = lambda t, s='body': Paragraph(t, S[s])
    H1 = lambda t: [SP(4), P(t, 'h1'), HR()]
    H2 = lambda t: [SP(2), P(t, 'h2')]
    H3 = lambda t: [P(t, 'h3')]
    LI = lambda t: P(f'• &nbsp;{t}', 'li')
    N  = lambda t: P(f'Note: {t}', 'note')
    CB = lambda *lines: code_block(list(lines), S)
    story = []

    # TOC
    story += [P('Table of Contents', 'h1'), HR()]
    toc_data = [
        ['Ch.', 'Title', 'Page'],
        ['1', 'Product Overview', '3'],
        ['2', 'System Requirements', '3'],
        ['3', 'Getting Started', '4'],
        ['4', 'Mask Management', '4'],
        ['5', 'Port Management', '6'],
        ['6', 'EdgeLink SDK', '8'],
        ['7', 'FAQ / Troubleshooting', '9'],
    ]
    story.append(tbl(toc_data, [1.5*cm, 11*cm, 2*cm], S))
    story.append(PageBreak())

    # Ch1
    story += H1('1. Product Overview')
    story.append(P(
        'EdgeLink Server is an industrial IoT data relay application built with Unity Engine. '
        'It receives raw data from IoT devices via TCP or UDP, applies configurable '
        'parsing rules (Masks) to transform the data, and forwards structured messages '
        'to Unity applications (e.g. VR training, digital twin) through the EdgeLink SDK.',
        'body'))
    story.append(SP())
    story += H2('Key Features')
    for item in [
        'Multi-protocol: TCP Server, TCP Client, UDP',
        'Mask system: text-mode and binary-mode parsing with custom output templates',
        'Browser-based Web UI for configuration (http://localhost:8181)',
        'Real-time Monitor Log per port',
        'EdgeLink SDK: .NET Standard 2.0 DLL ready for Unity',
        'Multi-language UI: Traditional Chinese, English, Japanese',
    ]:
        story.append(LI(item))
    story.append(SP())
    story += H2('Data Flow')
    flow_data = [
        ['IoT Device', '→', 'EdgeLink Server', '→', 'Unity App'],
        ['Sensor / PLC', 'TCP / UDP', 'Parse + Format', 'EdgeLink SDK', 'Game / VR / Digital Twin'],
    ]
    story.append(tbl(flow_data, [3*cm, 1.5*cm, 4*cm, 3.5*cm, 4*cm], S))
    story.append(SP(10))

    # Ch2
    story += H1('2. System Requirements')
    req_data = [
        ['Item', 'Requirement'],
        ['OS', 'Windows 10 / 11  64-bit'],
        ['RAM', '4 GB minimum (8 GB recommended)'],
        ['Network', 'TCP/IP capable LAN or loopback'],
        ['Browser (Web UI)', 'Google Chrome, Microsoft Edge, Firefox (latest)'],
        ['Unity (developers)', 'Unity 2020.3 LTS or later with .NET Standard 2.0'],
    ]
    story.append(tbl(req_data, [5*cm, 10.5*cm], S))
    story.append(SP(10))

    # Ch3
    story += H1('3. Getting Started')
    story += H2('3.1 Launch the Application')
    story.append(LI('Run EdgeLinkServer.exe (or press Play in the Unity Editor)'))
    story.append(LI('The HTTP server will start automatically on port 8181'))
    story.append(LI('The Unity main window shows the connection status indicator'))
    story.append(SP())
    story += H2('3.2 Open the Web Management UI')
    story.append(P('Open a browser on the same machine and navigate to:', 'body'))
    story.append(CB('http://localhost:8181'))
    story.append(LI('Left panel: mask list with add / import controls'))
    story.append(LI('Right top: mask definition editor (visible after selecting a mask)'))
    story.append(LI('Right bottom: port status table and Monitor Log'))
    story.append(N('The Web UI currently only accepts local connections (localhost).'))
    story.append(SP(10))

    # Ch4
    story += H1('4. Mask Management')
    story += H2('4.1 What is a Mask?')
    story.append(P(
        'A Mask defines two things: (1) how to parse the raw string or binary data '
        'received from an IoT device; and (2) how to format the parsed result into an '
        'output string for downstream consumers. Each port can be assigned one mask. '
        'The built-in mask OriginalData passes raw data through unchanged.',
        'body'))
    story.append(SP())

    story += H2('4.2 Mask List Operations')
    mask_ops = [
        ['Action', 'Description'],
        ['Click mask name',   'Open the definition editor on the right'],
        ['Add Mask',          'Enter a Mask ID (required) and optional localization key, then click Add Mask'],
        ['Delete Mask',       'Click the × icon next to the mask name (OriginalData cannot be deleted)'],
        ['Import JSON',       'Click the upload icon and select a previously exported SDK JSON file'],
        ['Export SDK JSON',   'Click "Export SDK JSON" in the editor header to download the definition'],
    ]
    story.append(tbl(mask_ops, [4.5*cm, 11*cm], S))
    story.append(SP())

    story += H2('4.3 Text Mode')
    story.append(P('For IoT devices sending human-readable text (e.g. NMEA sentences, custom KV formats).', 'body'))
    story += H3('Step 1 — Paste Sample Data')
    story.append(LI('Paste a real example string from your IoT device (e.g. ID:1;TEMP:25.3;HUM:60)'))
    story.append(LI('Set the Field Delimiter (default ;) and KV Separator (default :)'))
    story.append(LI('Click Auto Detect to let the system infer delimiters automatically'))
    story.append(LI('Parsed fields appear as yellow tags below'))
    story += H3('Step 2 — Define Output Template')
    story.append(LI('Use {FieldName} placeholders, e.g. ID={ID}&T={TEMP}'))
    story.append(LI('{raw} outputs the original string unmodified'))
    story.append(LI('Click a blue field tag to insert its placeholder at the cursor position'))
    story += H3('Step 3 — Live Preview')
    story.append(LI('Left side shows the input; right side shows the formatted output in real time'))
    story.append(LI('Orange warning if a placeholder has no matching field'))
    story.append(SP())

    story += H2('4.4 Binary Mode')
    story.append(P('For MCUs sending raw binary packets.', 'body'))
    story.append(LI('Click "+ Add Field", enter name, byte offset, length, and data type'))
    story.append(SP(2))
    dtype_data = [
        ['Type', 'Size', 'Description'],
        ['uint8',    '1 B',  'Unsigned 8-bit integer'],
        ['uint16_le','2 B',  'Unsigned 16-bit integer, little-endian'],
        ['uint16_be','2 B',  'Unsigned 16-bit integer, big-endian'],
        ['int32_le', '4 B',  'Signed 32-bit integer, little-endian'],
        ['float_le', '4 B',  'IEEE 754 single-precision float, little-endian'],
        ['hex',      'any',  'Raw bytes as hex string (e.g. 1A2B3C)'],
    ]
    story.append(tbl(dtype_data, [2.5*cm, 1.5*cm, 11.5*cm], S))
    story.append(N('Binary mode preview requires a real device; the Web UI does not simulate binary input.'))
    story.append(SP())

    story += H2('4.5 Save & Export')
    story.append(LI('Click "Save" to persist the definition to the server (Setting/MaskDefinitions.setting)'))
    story.append(LI('Click "Export SDK JSON" to download mask_<ID>.json for the EdgeLink SDK'))
    story.append(LI('Place the exported JSON in the Unity project under Assets/StreamingAssets/'))
    story.append(SP(10))

    # Ch5
    story += H1('5. Port Management')
    story += H2('5.1 Protocol Types')
    proto_data = [
        ['Protocol', 'Role', 'Use Case'],
        ['TCP Server', 'Passive listener', 'IoT device initiates connection to this machine; supports multiple clients'],
        ['TCP Client', 'Active connector', 'This machine connects out to a remote IoT device or broker'],
        ['UDP',        'Connectionless',   'Receive on a port; optionally forward to a target IP'],
    ]
    story.append(tbl(proto_data, [2.5*cm, 2.5*cm, 10.5*cm], S))
    story.append(SP())

    story += H2('5.2 Add a Port')
    story.append(P('Click "Add Port" and fill in the fields according to the chosen protocol:', 'body'))
    story.append(SP(4))
    field_data = [
        ['Field', 'TCP Server', 'TCP Client', 'UDP'],
        ['Name',         'Required',       'Required',           'Required'],
        ['Local Port',   'Listen port',    '(auto-assigned)',    'Forward destination port'],
        ['Remote Port',  '—',              'Target port',        'Receive (listen) port'],
        ['Target IP',    '—',              'Required',           'Optional (empty = broadcast)'],
        ['Mask',         'Optional',       'Optional',           'Optional'],
    ]
    story.append(tbl(field_data, [3*cm, 3*cm, 3*cm, 5.5*cm], S))
    story.append(SP(4))
    story.append(N('For UDP with no Target IP, received data is broadcast to the local port.'))
    story.append(SP())

    story += H2('5.3 Connection Status')
    status_data = [
        ['Status', 'Meaning'],
        ['🟢 Connected',    'TCP: connection established  |  UDP: port bound successfully'],
        ['⚪ Disconnected', 'TCP: waiting for connection or disconnected  |  UDP: normal idle state'],
    ]
    story.append(tbl(status_data, [3*cm, 12.5*cm], S))
    story.append(SP())

    story += H2('5.4 Monitor Log')
    story.append(LI('Click the Monitor button next to a port to open the real-time log panel'))
    story.append(LI('Each received message (after mask processing) is appended in real time'))
    story.append(LI('Use the search box to filter log lines by keyword (case-insensitive)'))
    story.append(LI('The scroll-lock button pauses auto-scrolling for reviewing history'))
    story.append(LI('The Clear button empties the displayed log (historical data is not deleted)'))
    story.append(LI('Only one port can be monitored at a time; switching port closes the previous stream'))
    story.append(SP(10))

    # Ch6
    story += H1('6. EdgeLink SDK')
    story += H2('6.1 Installation')
    story.append(LI('Copy EdgeLinkSdk.dll into your Unity project\'s Assets/Plugins/ folder'))
    story.append(LI('Copy the exported mask JSON into Assets/StreamingAssets/'))
    story.append(N('EdgeLinkSdk.dll has zero external dependencies — only a single DLL is required.'))
    story.append(SP())

    story += H2('6.2 Quick Start')
    story.append(P('Create a MonoBehaviour and attach it to any GameObject:', 'body'))
    story.append(CB(
        'using System.IO;',
        'using EdgeLink;',
        'using UnityEngine;',
        '',
        'public class MyReceiver : MonoBehaviour',
        '{',
        '    EdgeLinkReceiver _receiver;',
        '    MaskParser       _parser;',
        '',
        '    void Start()',
        '    {',
        '        string path = Path.Combine(',
        '            Application.streamingAssetsPath, "sensor.json");',
        '        var mask  = MaskDefinition.FromJsonFile(path);',
        '        _parser   = new MaskParser(mask);',
        '',
        '        _receiver = new EdgeLinkReceiver(Protocol.TCP, mask);',
        '        _receiver.OnMessage += OnMessage;',
        '        _receiver.Start(9090); // listen port',
        '    }',
        '',
        '    void Update()   => _receiver?.Flush(); // REQUIRED – fires events on main thread',
        '    void OnDestroy()=> _receiver?.Dispose();',
        '',
        '    void OnMessage(IotMessage msg)',
        '    {',
        '        var fields = _parser.ParseOutput(msg.Raw);',
        '        if (fields.TryGetValue("TEMP", out string temp))',
        '            Debug.Log($"Temperature: {temp}");',
        '    }',
        '}',
    ))
    story.append(SP())

    story += H2('6.3 API Reference')
    api_data = [
        ['Class / Enum', 'Description'],
        ['EdgeLinkReceiver', 'Sets up a TCP or UDP listener and receives data from IoT Server'],
        ['MaskDefinition',   'Mask definition; load from JSON file or JSON string'],
        ['MaskParser',       'Reverse-parses IoT Server output strings back into field-value pairs'],
        ['IotMessage',       'Received message; msg.Raw contains the full formatted output string'],
        ['Protocol',         'Enum: Protocol.TCP or Protocol.UDP'],
    ]
    story.append(tbl(api_data, [4.5*cm, 11*cm], S))
    story.append(SP(4))

    story += H2('6.4 Important Notes')
    story.append(LI('EdgeLinkReceiver.Flush() MUST be called every frame in Update() for events to fire on the main thread'))
    story.append(LI('In the IoT Server Web UI, add a TCP Client port pointing to the Unity machine\'s IP and the listenPort passed to Start()'))
    story.append(LI('The mask JSON exported from Web UI and the mask used by EdgeLinkReceiver must match'))
    story.append(SP(10))

    # Ch7
    story += H1('7. FAQ / Troubleshooting')
    faq = [
        ('Web UI shows "Connection Failed"',
         'Ensure EdgeLinkServer.exe is running (or Unity is in Play Mode). '
         'Navigate to http://localhost:8181. The status indicator should turn green.'),
        ('Port status stays "Disconnected"',
         'TCP Client: verify the target device IP and port, and that the device is listening. '
         'TCP Server: wait for the IoT device to connect to your machine\'s IP and the configured port. '
         'UDP: UDP is connectionless; check that data is being received in Monitor Log instead.'),
        ('Mask definitions disappear after restart',
         'Definitions are saved to Setting/MaskDefinitions.setting. '
         'Ensure the application has write permissions. '
         'Also export SDK JSON files as backups.'),
        ('EdgeLink SDK shows compile errors in Unity',
         'Confirm EdgeLinkSdk.dll is in Assets/Plugins/. '
         'No additional dependencies are required. '
         'Try right-clicking Assets/Plugins → Reimport.'),
        ('Monitor Log shows no data',
         'Verify the IoT device is actively sending data and that the port protocol and number match. '
         'Use a tool like netcat or Packet Sender to manually send test data.'),
    ]
    for q, a in faq:
        story += H3(f'Q: {q}')
        story.append(P(f'A: {a}', 'body'))
        story.append(SP(4))

    return story


# ── Build PDF ─────────────────────────────────────────────────
def build_pdf(filename, title, company, cover_args, story_fn, cjk=False):
    os.makedirs(os.path.dirname(filename), exist_ok=True)
    _setup_fonts(cjk)
    S = _styles()

    doc = _DocTemplate(
        filename,
        pagesize=A4,
        leftMargin=2*cm, rightMargin=2*cm,
        topMargin=2.2*cm, bottomMargin=1.8*cm,
        title=title, company=company,
    )

    story = _cover(S, *cover_args)
    story += story_fn(S)
    doc.build(story)
    print(f'  OK {filename}')


# ── Main ──────────────────────────────────────────────────────
def main():
    import datetime
    date = datetime.date.today().strftime('%Y-%m-%d')

    print('Generating manuals …')

    build_pdf(
        filename='docs/EdgeLinkServer_Manual_zh-TW.pdf',
        title='EdgeLink Server 使用說明書',
        company='雲和科技 Yunhe Tech',
        cover_args=(
            'EdgeLink Server',                 # title_line1
            '使用說明書',                       # title_line2
            '工業 IoT 數據中繼伺服器',           # subtitle
            'v1.1.1',                          # version
            '雲和科技 Yunhe Tech',               # company
            date,                              # date
        ),
        story_fn=content_zhtw,
        cjk=True,
    )

    build_pdf(
        filename='docs/EdgeLinkServer_Manual_en-US.pdf',
        title='EdgeLink Server User Manual',
        company='Yunhe Tech',
        cover_args=(
            'EdgeLink Server',                 # title_line1
            'User Manual',                     # title_line2
            'Industrial IoT Data Relay Server',# subtitle
            'v1.1.1',                          # version
            'Yunhe Tech',                      # company
            date,                              # date
        ),
        story_fn=content_enus,
        cjk=False,
    )

    print('Done.')


if __name__ == '__main__':
    main()
