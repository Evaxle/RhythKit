; ============================================================
; RhythKit Colorset Maker - Fully Corrected Version (AutoHotkey v1)
; ============================================================

#SingleInstance Force
SetWorkingDir, %A_ScriptDir%

; -------------------------
; Settings / Paths
; -------------------------
settingsFile := A_ScriptDir . "\RhythKit_Settings.ini"
defaultPath  := A_ScriptDir . "\colorsets"
colorsetsPath := defaultPath

IfExist, %settingsFile%
    IniRead, colorsetsPath, %settingsFile%, Settings, colorsets_path, %defaultPath%

colorsetsPath := Trim(colorsetsPath)
if (colorsetsPath = "")
    colorsetsPath := defaultPath

; -------------------------
; Theme / State
; -------------------------
bgColor      := 0xEDEDED
accentBlue   := 0x4CC3FF
accentPink   := 0xFF4CCF
accentPurple := 0xA44CFF
textColor    := 0x000000
mutedColor   := 0x6E6E6E

noteCount    := 1
noteColors   := {}
noteHwnds    := {}
noteHbitmaps := {}
noteHwndMap  := {}
activeNote   := 1

wheelSize    := 220
wheelRadius  := 107
wheelHBitmap := 0
logoHBitmap  := 0
logoPicHWND  := 0
wheelPicHWND := 0

csCtrls := ["CsTitle","CsCountLabel","CountEdit","CsGridLabel","CsWheelLabel","CsPathLabel","CsPathEdit","CsBrowseBtn","CsNameLabel","CsNameEdit","CsDownloadBtn","CsStatus"]
stCtrls := ["StTitle","StPathLabel","StPathEdit","StBrowseBtn","StSaveBtn","StStatus"]

; -------------------------
; Start
; -------------------------
Gdip_Startup()
BuildMainGUI()
Gui, Show, w940 h600
RebuildSquares()
return

; ============================================================
; GUI BUILD
; ============================================================
BuildMainGUI() {
    global bgColor, accentBlue, textColor, mutedColor
    global wheelPicHWND, wheelHBitmap, logoPicHWND, logoHBitmap
    global colorsetsPath, wheelSize
    global csCtrls, stCtrls

    bgHex         := HexColor(bgColor)
    textHex       := HexColor(textColor)
    mutedHex      := HexColor(mutedColor)
    accentBlueHex := HexColor(accentBlue)

    Gui, +Resize
    Gui, Color, %bgHex%
    Gui, Font, s11 c%textHex%, Segoe UI

    ; Logo
    Gui, Add, Picture, x8 y8 w170 h70 hwndlogoPicHWND
    logoHBitmap := DrawLogo()
    SetControlImage(logoPicHWND, logoHBitmap)

    ; Left column
    Gui, Add, Button, x10 y90 w150 h30 gGotoColorset, Colorset Maker
    Gui, Add, Button, x10 y130 w150 h30 gGotoSettings, Settings
    Gui, Add, Text, x10 y520 w150 h16 c%mutedHex%, RhythKit Toolkit

    ; Colorset section
    Gui, Font, s14 c%accentBlueHex%
    Gui, Add, Text, x185 y12 w300 h24 vCsTitle, Colorset Maker

    Gui, Font, s11 c%textHex%
    Gui, Add, Text, x185 y50 w120 h20 vCsCountLabel, Number of Notes:
    Gui, Add, Edit, x305 y46 w70 h24 vCountEdit gCountChanged, 1

    Gui, Add, Text, x185 y80 w520 h16 vCsGridLabel c%mutedHex%, Notes: (5 per row - click a note, then pick a color on the wheel)

    ; Hue wheel
    Gui, Add, Text, x640 y30 w220 h20 vCsWheelLabel c%accentBlueHex%, Hue Wheel
    Gui, Add, Picture, x640 y52 w220 h220 gWheelClick hwndwheelPicHWND
    wheelBmp := CreateWheel(wheelSize)
    wheelHBitmap := Gdip_CreateHBITMAPFromBitmap(wheelBmp)
    SetControlImage(wheelPicHWND, wheelHBitmap)
    Gdip_DisposeImage(wheelBmp)

    ; Path / name / download
    Gui, Add, Text, x185 y350 w220 h18 vCsPathLabel c%mutedHex%, Colorsets Path:
    Gui, Add, Edit, x185 y372 w320 h24 vCsPathEdit, %colorsetsPath%
    Gui, Add, Button, x515 y372 w80 h24 gBrowsePaths vCsBrowseBtn, Browse

    Gui, Add, Text, x185 y410 w220 h18 vCsNameLabel c%mutedHex%, Colorset Name:
    Gui, Add, Edit, x185 y432 w320 h24 vCsNameEdit

    Gui, Add, Button, x185 y470 w200 h34 gDownloadColorset vCsDownloadBtn, Download Colorset
    Gui, Add, Text, x185 y510 w520 h20 vCsStatus c%accentBlueHex%, Ready.

    ; Settings section
    Gui, Add, Text, x185 y12 w300 h24 vStTitle c%accentBlueHex%, Settings
    Gui, Add, Text, x185 y50 w220 h20 vStPathLabel c%mutedHex%, Colorsets Path:
    Gui, Add, Edit, x185 y72 w320 h24 vStPathEdit, %colorsetsPath%
    Gui, Add, Button, x515 y70 w80 h24 gBrowsePaths vStBrowseBtn, Browse
    Gui, Add, Button, x185 y110 w200 h30 gSaveSettings vStSaveBtn, Save Settings
    Gui, Add, Text, x185 y156 w400 h20 vStStatus c%accentBlueHex%,

    ShowSection(1)
}

; ============================================================
; SQUARE GRID
; ============================================================
RebuildSquares() {
    global noteCount, noteColors, noteHwnds, noteHbitmaps, noteHwndMap, activeNote

    ; Delete old squares safely
    Loop, 50 {
        varName := "Sq" . A_Index
        GuiControlGet, hCtrl, Hwnd, %varName%
        if (hCtrl)
            GuiControl, Delete, %varName%
    }

    noteHwnds := {}
    noteHbitmaps := {}
    noteHwndMap := {}

    if (activeNote < 1 || activeNote > noteCount)
        activeNote := 1

    Loop, %noteCount% {
        i := A_Index
        if (!noteColors.HasKey(i))
            noteColors[i] := 0xFFFFFFFF

        col := Mod(i - 1, 5)
        row := (i - 1) // 5
        x := 185 + col * 52
        y := 110 + row * 52

        varName := "Sq" . i
        Gui, Add, Picture, x%x% y%y% w40 h40 v%varName% gNoteClick
        GuiControlGet, hCtrl, Hwnd, %varName%

        noteHwnds[i] := hCtrl
        noteHwndMap[hCtrl] := i

        SetNoteColor(i, noteColors[i], (i = activeNote))
    }
}

; ============================================================
; SECTION SWITCHING
; ============================================================
ShowSection(sec) {
    global csCtrls, stCtrls, noteHwnds, wheelPicHWND

    if (sec = 1) {
        for _, v in csCtrls
            GuiControl, Show, %v%
        for _, v in stCtrls
            GuiControl, Hide, %v%
        GuiControl, Show, %wheelPicHWND%
        for _, h in noteHwnds
            GuiControl, Show, %h%
    } else {
        for _, v in csCtrls
            GuiControl, Hide, %v%
        for _, v in stCtrls
            GuiControl, Show, %v%
        GuiControl, Hide, %wheelPicHWND%
        for _, h in noteHwnds
            GuiControl, Hide, %h%
    }
}

; ============================================================
; EVENTS
; ============================================================
CountChanged:
    GuiControlGet, val, , CountEdit
    if val is not integer
        return
    val := val + 0
    if (val < 1)
        val := 1
    if (val > 50)
        val := 50
    noteCount := val
    GuiControl, , CountEdit, %val%
    RebuildSquares()
return

NoteClick:
    global activeNote, noteHwndMap, noteColors
    GuiControlGet, hN, Hwnd, %A_GuiControl%
    i := noteHwndMap[hN]
    if (i = "")
        return
    SetNoteColor(activeNote, noteColors[activeNote], false)
    activeNote := i
    SetNoteColor(i, noteColors[i], true)
return

WheelClick:
    global activeNote, wheelPicHWND, wheelSize, wheelRadius, noteColors
    GuiControlGet, wPos, Pos, %wheelPicHWND%
    cx := wPosX + wheelSize/2
    cy := wPosY + wheelSize/2
    dx := A_GuiX - cx
    dy := A_GuiY - cy
    dist := Sqrt(dx*dx + dy*dy)
    if (dist > wheelRadius)
        dist := wheelRadius
    ang := ATan2(dy, dx)
    hue := Mod(ang * 57.29578, 360)
    if (hue < 0)
        hue += 360
    sat := dist / wheelRadius
    rgb := HsvToRgb(hue, sat, 1)
    color := (0xFF << 24) | (rgb[1]<<16) | (rgb[2]<<8) | rgb[3]
    noteColors[activeNote] := color
    SetNoteColor(activeNote, color, true)
return

GuiSize:
    if (A_EventInfo = 1)
        return
    global noteCount, noteHwnds, noteColors, activeNote

    baseX := 185
    baseY := 110

    Gui, +LastFound
    WinGetPos, , , winW, winH

    sqSize := winW / 30
    if (sqSize < 30)
        sqSize := 30
    if (sqSize > 80)
        sqSize := 80

    Loop, %noteCount% {
        i := A_Index
        col := Mod(i - 1, 5)
        row := (i - 1) // 5
        x := baseX + col * (sqSize + 12)
        y := baseY + row * (sqSize + 12)
        GuiControl, Move, % noteHwnds[i], x%x% y%y% w%sqSize% h%sqSize%
        SetNoteColor(i, noteColors[i], (i = activeNote))
    }
return

GotoColorset:
    ShowSection(1)
return

GotoSettings:
    ShowSection(2)
return

BrowsePaths:
    global colorsetsPath, settingsFile
    FileSelectFolder, sel, %colorsetsPath%, 3
    if (sel != "") {
        colorsetsPath := sel
        GuiControl, , CsPathEdit, %sel%
        GuiControl, , StPathEdit, %sel%
        IniWrite, %sel%, %settingsFile%, Settings, colorsets_path
        GuiControl, , CsStatus, Path updated.
    }
return

SaveSettings:
    global colorsetsPath, settingsFile
    GuiControlGet, p2, , StPathEdit
    p2 := Trim(p2)
    if (p2 = "") {
        GuiControl, , StStatus, Please enter a path.
        return
    }
    colorsetsPath := p2
    GuiControl, , CsPathEdit, %p2%
    IniWrite, %p2%, %settingsFile%, Settings, colorsets_path
    GuiControl, , StStatus, Settings saved.
return

DownloadColorset:
    global noteCount, noteColors
    GuiControlGet, path, , CsPathEdit
    GuiControlGet, nm, , CsNameEdit
    path := Trim(path)
    nm := Trim(nm)
    if (path = "") {
        GuiControl, , CsStatus, Please set a path first.
        return
    }
    if !FileExist(path)
        FileCreateDir, %path%
    if (nm = "") {
        FormatTime, ts, , yyyyMMdd_HHmmss
        nm := "colorset_" . ts
    }
    if !RegExMatch(nm, "i)\.txt$")
        nm := nm . ".txt"

    text := ""
    Loop, %noteCount% {
        color := noteColors[A_Index]
        r := (color>>16)&0xFF
        g := (color>>8)&0xFF
        b := color&0xFF
        text .= Format("#{1:02X}{2:02X}{3:02X}", r,g,b) . "`r`n"
    }

    fullPath := path . "\" . nm
    FileDelete, %fullPath%
    FileAppend, %text%, %fullPath%
    GuiControl, , CsStatus, Saved to %fullPath%
return

GuiClose:
GuiEscape:
    global noteHbitmaps, wheelHBitmap, logoHBitmap
    for _, hb in noteHbitmaps
        if (hb)
            DllCall("DeleteObject", "Ptr", hb)
    if (wheelHBitmap)
        DllCall("DeleteObject", "Ptr", wheelHBitmap)
    if (logoHBitmap)
        DllCall("DeleteObject", "Ptr", logoHBitmap)
    Gdip_Shutdown()
    ExitApp
return

; ============================================================
; HELPERS
; ============================================================
HexColor(num) {
    return Format("{:06X}", num & 0xFFFFFF)
}

SetNoteColor(i, color, active) {
    global noteColors, noteHwnds, noteHbitmaps
    noteColors[i] := color
    border := active ? 0xFF000000 : 0xFFB0B0B0

    GuiControlGet, pos, Pos, % noteHwnds[i]
    w := posW
    h := posH

    bmp := CreateColorBitmap(color, w, h, border)
    hb := Gdip_CreateHBITMAPFromBitmap(bmp)

    old := noteHbitmaps[i]
    if (old)
        DllCall("DeleteObject", "Ptr", old)

    noteHbitmaps[i] := hb
    SetControlImage(noteHwnds[i], hb)
    Gdip_DisposeImage(bmp)
}

SetControlImage(ctrlHwnd, hbmp) {
    SendMessage, 0x0172, 0, %hbmp%, , ahk_id %ctrlHwnd%
}

CreateColorBitmap(color, w, h, border) {
    bmp := Gdip_CreateBitmap(w, h)
    gfx := Gdip_GraphicsFromImage(bmp)
    Gdip_SetSmoothingMode(gfx, 4)

    brush := Gdip_CreateBrushSolid(color)
    Gdip_FillRectangle(gfx, brush, 2, 2, w-4, h-4)
    Gdip_DeleteBrush(brush)

    pen := Gdip_CreatePen(border, 3)
    Gdip_DrawRectangle(gfx, pen, 2, 2, w-4, h-4)
    Gdip_DeletePen(pen)

    Gdip_DeleteGraphics(gfx)
    return bmp
}

ATan2(y, x) {
    if (x > 0)
        return ATan(y/x)
    if (x < 0 && y >= 0)
        return ATan(y/x) + 3.141592653589793
    if (x < 0 && y < 0)
        return ATan(y/x) - 3.141592653589793
    if (x = 0 && y > 0)
        return 3.141592653589793/2
    if (x = 0 && y < 0)
        return -3.141592653589793/2
    return 0
}

HsvToRgb(h, s, v) {
    c := v*s
    x := c*(1 - Abs(Mod(h/60,2)-1))
    m := v-c

    if (h < 60)
        r:=c, g:=x, b:=0
    else if (h < 120)
        r:=x, g:=c, b:=0
    else if (h < 180)
        r:=0, g:=c, b:=x
    else if (h < 240)
        r:=0, g:=x, b:=c
    else if (h < 300)
        r:=x, g:=0, b:=c
    else
        r:=c, g:=0, b:=x

    return [Round((r+m)*255), Round((g+m)*255), Round((b+m)*255)]
}

CreateWheel(size) {
    bmp := Gdip_CreateBitmap(size, size)
    gfx := Gdip_GraphicsFromImage(bmp)
    Gdip_SetSmoothingMode(gfx, 4)

    center := size/2
    radius := size/2 - 3

    Loop, 360 {
        ang := (A_Index-1)*3.141592653589793/180
        x1 := center + Cos(ang)*3
        y1 := center + Sin(ang)*3
        x2 := center + Cos(ang)*radius
        y2 := center + Sin(ang)*radius

        rgb := HsvToRgb(A_Index - 1, 1.0, 1.0)
        color := (0xFF << 24) | (rgb[1] << 16) | (rgb[2] << 8) | rgb[3]

        pen := Gdip_CreatePen(color, 2)
        Gdip_DrawLine(gfx, pen, x1, y1, x2, y2)
        Gdip_DeletePen(pen)
    }

    ; Inner fade
    Loop, % (radius + 1) {
        i := A_Index - 1
        r := radius - i
        alpha := Round((i / radius) * 255)
        brush := Gdip_CreateBrushSolid((alpha << 24) | 0xFFFFFF)
        Gdip_FillEllipse(gfx, brush, center - r, center - r, r*2, r*2)
        Gdip_DeleteBrush(brush)
    }

    ; Outer ring
    pen := Gdip_CreatePen(0xFFB0B0B0, 2)
    Gdip_DrawEllipse(gfx, pen, 2, 2, size - 4, size - 4)
    Gdip_DeletePen(pen)

    ; Center dot
    brush := Gdip_CreateBrushSolid(0xFFFFFFFF)
    Gdip_FillEllipse(gfx, brush, center - 3, center - 3, 6, 6)
    Gdip_DeleteBrush(brush)

    Gdip_DeleteGraphics(gfx)
    return bmp
}

; ============================================================
; LOGO DRAWING
; ============================================================
DrawLogo() {
    global accentBlue, accentPink, accentPurple

    bmp := Gdip_CreateBitmap(170, 70)
    gfx := Gdip_GraphicsFromImage(bmp)
    Gdip_SetSmoothingMode(gfx, 4)

    ; Background glow
    brush := Gdip_CreateBrushSolid(0x22FFFFFF)
    Gdip_FillEllipse(gfx, brush, -60, -20, 250, 130)
    Gdip_DeleteBrush(brush)

    ; Square icon
    pen := Gdip_CreatePen(accentBlue, 3)
    Gdip_DrawRoundedRectangle(gfx, pen, 8, 14, 34, 34, 8)
    Gdip_DeletePen(pen)

    ; Curves
    p1 := Gdip_CreatePen(accentPurple, 4)
    p2 := Gdip_CreatePen(accentPink, 4)
    Gdip_DrawCurve(gfx, p1, [48,18, 66,30, 84,40])
    Gdip_DrawCurve(gfx, p2, [84,40, 100,30, 116,18])
    Gdip_DeletePen(p1)
    Gdip_DeletePen(p2)

    ; Dots
    brush := Gdip_CreateBrushSolid(accentBlue)
    Gdip_FillRectangle(gfx, brush, 128, 10, 6, 6)
    Gdip_FillRectangle(gfx, brush, 118, 30, 8, 8)
    Gdip_FillRectangle(gfx, brush, 108, 16, 5, 5)
    Gdip_DeleteBrush(brush)

    ; Text
    Gdip_DrawString(gfx, "RhythKit", "Segoe UI", 15, 0xFF000000, 44, 26)

    Gdip_DeleteGraphics(gfx)
    hb := Gdip_CreateHBITMAPFromBitmap(bmp)
    Gdip_DisposeImage(bmp)
    return hb
}

; ============================================================
; GDI+ WRAPPERS
; ============================================================
Gdip_Startup() {
    global __GdipToken
    if (!__GdipToken) {
        VarSetCapacity(GdipSI, 16, 0)
        NumPut(1, GdipSI, 0, "UInt")
        DllCall("gdiplus\GdiplusStartup", "Ptr*", __GdipToken, "Ptr", &GdipSI, "Ptr", 0)
    }
    return __GdipToken
}

Gdip_Shutdown() {
    global __GdipToken
    if (__GdipToken) {
        DllCall("gdiplus\GdiplusShutdown", "Ptr", __GdipToken)
        __GdipToken := 0
    }
}

Gdip_CreateBitmap(w, h) {
    Gdip_Startup()
    DllCall("gdiplus\GdipCreateBitmapFromScan0", "Int", w, "Int", h, "Int", 0, "Int", 0x26200A, "Ptr", 0, "Ptr*", pBitmap)
    return pBitmap
}

Gdip_GraphicsFromImage(pBitmap) {
    DllCall("gdiplus\GdipGetImageGraphicsContext", "Ptr", pBitmap, "Ptr*", gfx)
    return gfx
}

Gdip_SetSmoothingMode(gfx, mode) {
    DllCall("gdiplus\GdipSetSmoothingMode", "Ptr", gfx, "Int", mode)
}

Gdip_CreatePen(color, width) {
    DllCall("gdiplus\GdipCreatePen1", "UInt", color, "Float", width, "Int", 2, "Ptr*", pen)
    return pen
}

Gdip_DeletePen(pen) {
    DllCall("gdiplus\GdipDeletePen", "Ptr", pen)
}

Gdip_CreateBrushSolid(color) {
    DllCall("gdiplus\GdipCreateSolidFill", "UInt", color, "Ptr*", brush)
    return brush
}

Gdip_DeleteBrush(brush) {
    DllCall("gdiplus\GdipDeleteBrush", "Ptr", brush)
}

Gdip_DrawRoundedRectangle(gfx, pen, x, y, w, h, r) {
    Gdip_DrawCurve(gfx, pen, [x+r,y, x+w-r,y])
    Gdip_DrawCurve(gfx, pen, [x+w,y+r, x+w,y+h-r])
    Gdip_DrawCurve(gfx, pen, [x+w-r,y+h, x+r,y+h])
    Gdip_DrawCurve(gfx, pen, [x,y+h-r, x,y+r])
}

Gdip_DrawCurve(gfx, pen, pts) {
    len := pts.Length()
    VarSetCapacity(arr, 8 * len, 0)
    i := 0
    for k, v in pts {
        NumPut(v, arr, i*8, "Float")
        i++
    }
    DllCall("gdiplus\GdipDrawCurve", "Ptr", gfx, "Ptr", pen, "Ptr", &arr, "Int", len)
}

Gdip_DrawLine(gfx, pen, x1, y1, x2, y2) {
    DllCall("gdiplus\GdipDrawLine", "Ptr", gfx, "Ptr", pen, "Float", x1, "Float", y1, "Float", x2, "Float", y2)
}

Gdip_DrawRectangle(gfx, pen, x, y, w, h) {
    DllCall("gdiplus\GdipDrawRectangle", "Ptr", gfx, "Ptr", pen, "Float", x, "Float", y, "Float", w, "Float", h)
}

Gdip_FillRectangle(gfx, brush, x, y, w, h) {
    DllCall("gdiplus\GdipFillRectangle", "Ptr", gfx, "Ptr", brush, "Float", x, "Float", y, "Float", w, "Float", h)
}

Gdip_DrawEllipse(gfx, pen, x, y, w, h) {
    DllCall("gdiplus\GdipDrawEllipse", "Ptr", gfx, "Ptr", pen, "Float", x, "Float", y, "Float", w, "Float", h)
}

Gdip_FillEllipse(gfx, brush, x, y, w, h) {
    DllCall("gdiplus\GdipFillEllipse", "Ptr", gfx, "Ptr", brush, "Float", x, "Float", y, "Float", w, "Float", h)
}

Gdip_DrawString(gfx, text, fontName, size, color, x, y) {
    DllCall("gdiplus\GdipCreateFontFamilyFromName", "WStr", fontName, "Ptr", 0, "Ptr*", pFamily)
    DllCall("gdiplus\GdipCreateFont", "Ptr", pFamily, "Float", size, "Int", 0, "Int", 2, "Ptr*", pFont)
    DllCall("gdiplus\GdipCreateSolidFill", "UInt", color, "Ptr*", pBrush)

    VarSetCapacity(rect, 16, 0)
    NumPut(x, rect, 0, "Float")
    NumPut(y, rect, 4, "Float")
    NumPut(800, rect, 8, "Float")
    NumPut(150, rect, 12, "Float")

    DllCall("gdiplus\GdipDrawString", "Ptr", gfx, "WStr", text, "Int", StrLen(text), "Ptr", pFont, "Ptr", &rect, "Ptr", 0, "Ptr", pBrush)

    DllCall("gdiplus\GdipDeleteBrush", "Ptr", pBrush)
    DllCall("gdiplus\GdipDeleteFont", "Ptr", pFont)
    DllCall("gdiplus\GdipDeleteFontFamily", "Ptr", pFamily)
}

Gdip_CreateHBITMAPFromBitmap(pBitmap) {
    DllCall("gdiplus\GdipCreateHBITMAPFromBitmap", "Ptr", pBitmap, "Ptr*", hBitmap, "UInt", 0)
    return hBitmap
}

Gdip_DeleteGraphics(gfx) {
    DllCall("gdiplus\GdipDeleteGraphics", "Ptr", gfx)
}

Gdip_DisposeImage(img) {
    DllCall("gdiplus\GdipDisposeImage", "Ptr", img)
}
