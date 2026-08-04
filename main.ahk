#SingleInstance Force
SetWorkingDir, %A_ScriptDir%

settingsFile := A_ScriptDir . "\RhythKit_Settings.ini"
defaultPath  := "C:\Users\client\AppData\Roaming\CapoRhythia\skins\colorsets"
colorsetsPath := defaultPath
IfExist, %settingsFile%
    IniRead, colorsetsPath, %settingsFile%, Settings, colorsets_path, %defaultPath%
colorsetsPath := Trim(colorsetsPath)
if (colorsetsPath = "")
    colorsetsPath := defaultPath

bgColor      := 0x111318
accentBlue   := 0x4CC3FF
accentPink   := 0xFF4CCF
accentPurple := 0xA44CFF
textColor    := 0xFFFFFF
mutedColor   := 0x8A94A6

noteCount      := 5
noteColors     := {}    ; object of ARGB ints
noteHwnds      := {}    ; map index -> control hwnd
noteHbitmaps   := {}    ; map index -> HBITMAP
noteHwndMap    := {}    ; map hwnd -> index
activeNote     := 1
wheelSize      := 220
wheelRadius    := 107
wheelHBitmap   := 0
logoHBitmap    := 0
logoPicHWND    := 0
wheelPicHWND   := 0

; GUI v-variables must be global/static in AHK v1
CsTitle := ""
CsCountLabel := ""
CountEdit := ""
CsGridLabel := ""
CsWheelLabel := ""
CsPathLabel := ""
CsPathEdit := ""
CsBrowseBtn := ""
CsNameLabel := ""
CsNameEdit := ""
CsDownloadBtn := ""
CsStatus := ""

StTitle := ""
StPathLabel := ""
StPathEdit := ""
StBrowseBtn := ""
StSaveBtn := ""
StStatus := ""

Gdip_Startup()
BuildMainGUI()
return

BuildMainGUI() {
    global bgColor, accentBlue, textColor, mutedColor
    global wheelPicHWND, wheelHBitmap, logoPicHWND, logoHBitmap
    global noteHbitmaps, noteCount, colorsetsPath, wheelSize
    global CsTitle, CsCountLabel, CountEdit, CsGridLabel, CsWheelLabel, CsPathLabel, CsPathEdit, CsBrowseBtn, CsNameLabel, CsNameEdit, CsDownloadBtn, CsStatus
    global StTitle, StPathLabel, StPathEdit, StBrowseBtn, StSaveBtn, StStatus

    for k, hb in noteHbitmaps
        DllCall("DeleteObject", "Ptr", hb)
    noteHbitmaps := {}

    if (wheelHBitmap) {
        DllCall("DeleteObject", "Ptr", wheelHBitmap)
        wheelHBitmap := 0
    }
    if (logoHBitmap) {
        DllCall("DeleteObject", "Ptr", logoHBitmap)
        logoHBitmap := 0
    }

    Gui, Destroy
    Gui, New, +AlwaysOnTop +Resize, RhythKit Toolkit

    bgHex := HexColor(bgColor)
    textHex := HexColor(textColor)
    mutedHex := HexColor(mutedColor)
    accentBlueHex := HexColor(accentBlue)

    Gui, Color, %bgHex%
    Gui, Font, s11 c%textHex%, Segoe UI

    Gui, Add, Picture, x0 y0 w170 h70 hwndlogoPicHWND
    logoHBitmap := DrawLogo()
    if (logoHBitmap)
        SetControlImage(logoPicHWND, logoHBitmap)

    Gui, Add, Button, x10 y90 w150 h36 gGotoColorset, Colorset Maker
    Gui, Add, Button, x10 y132 w150 h36 gGotoSettings, Settings
    Gui, Add, Text, x10 y560 w150 h16 c%mutedHex%, RhythKit Toolkit

    ; compute rows without relying on Ceil/Floor
    rows := 0
    temp := noteCount
    while (temp > 0) {
        rows += 1
        temp -= 5
    }
    gridBottom := 110 + rows * 52

    Gui, Font, s14 c%accentBlueHex%, Segoe UI
    Gui, Add, Text, x185 y12 w300 h24 vCsTitle, Colorset Maker
    Gui, Font, s11 c%textHex%, Segoe UI
    Gui, Add, Text, x185 y50 w120 h20 vCsCountLabel, Number of Notes:
    Gui, Add, Edit, x305 y46 w70 h26 vCountEdit gCountChanged, %noteCount%
    Gui, Add, Text, x185 y80 w520 h16 vCsGridLabel c%mutedHex%, Notes: (5 per row - click a note, then pick a color on the wheel)

    Gui, Add, Text, x640 y30 w220 h20 vCsWheelLabel c%accentBlueHex%, Hue Wheel
    Gui, Add, Picture, x640 y52 w220 h220 gWheelClick hwndwheelPicHWND
    wheelBmp := CreateWheel(wheelSize)
    wheelHBitmap := Gdip_CreateHBITMAPFromBitmap(wheelBmp)
    if (wheelHBitmap)
        SetControlImage(wheelPicHWND, wheelHBitmap)
    Gdip_DisposeImage(wheelBmp)

    y1 := gridBottom + 10
    Gui, Add, Text, x185 y%y1% w220 h18 vCsPathLabel c%mutedHex%, Colorsets Path:
    y2 := y1 + 22
    Gui, Add, Edit, x185 y%y2% w320 h26 vCsPathEdit, %colorsetsPath%
    Gui, Add, Button, x515 y%y2% w80 h28 gBrowsePaths vCsBrowseBtn, Browse
    y3 := y2 + 34
    Gui, Add, Text, x185 y%y3% w220 h18 vCsNameLabel c%mutedHex%, Colorset Name:
    y4 := y3 + 22
    Gui, Add, Edit, x185 y%y4% w320 h26 vCsNameEdit
    y5 := y4 + 34
    Gui, Add, Button, x185 y%y5% w200 h40 gDownloadColorset vCsDownloadBtn, Download Colorset
    y6 := y5 + 46
    Gui, Add, Text, x185 y%y6% w520 h20 vCsStatus c%accentBlueHex%, Ready.

    Gui, Add, Text, x185 y12 w300 h24 vStTitle c%accentBlueHex%, Settings
    Gui, Add, Text, x185 y50 w220 h20 vStPathLabel c%mutedHex%, Colorsets Path:
    Gui, Add, Edit, x185 y72 w320 h26 vStPathEdit, %colorsetsPath%
    Gui, Add, Button, x515 y70 w80 h28 gBrowsePaths vStBrowseBtn, Browse
    Gui, Add, Button, x185 y110 w200 h36 gSaveSettings vStSaveBtn, Save Settings
    Gui, Add, Text, x185 y156 w400 h20 vStStatus c%accentBlueHex%,

    BuildNotes()

    winH := gridBottom + 230
    Gui, Show, w940 h%winH%
    ShowSection(1)
}

BuildNotes() {
    global noteCount, noteColors, noteHwnds, noteHbitmaps, noteHwndMap, activeNote

    noteHwnds := {}
    noteHbitmaps := {}
    noteHwndMap := {}
    activeNote := 1

    Loop, %noteCount% {
        i := A_Index
        if (!noteColors.HasKey(i))
            noteColors[i] := DefaultColor(i)
        col := Mod(i - 1, 5)
        row := (i - 1) // 5
        x := 185 + col * 52
        y := 110 + row * 52
        Gui, Add, Picture, x%x% y%y% w40 h40 gNoteClick hwndN
        noteHwnds[i] := N
        noteHwndMap[N] := i
        SetNoteColor(i, noteColors[i], (i = activeNote))
    }
}

ShowSection(sec) {
    global csCtrls, stCtrls, noteHwnds, wheelPicHWND
    if (sec = 1) {
        for k, v in csCtrls
            GuiControl, Show, %v%
        for k, v in stCtrls
            GuiControl, Hide, %v%
        GuiControl, Show, %wheelPicHWND%
        for k, h in noteHwnds
            GuiControl, Show, %h%
    } else {
        for k, v in csCtrls
            GuiControl, Hide, %v%
        for k, v in stCtrls
            GuiControl, Show, %v%
        GuiControl, Hide, %wheelPicHWND%
        for k, h in noteHwnds
            GuiControl, Hide, %h%
    }
}

CountChanged:
    GuiControlGet, val, , CountEdit
    if val is not integer
        return
    val := val + 0
    if (val < 1)
        val := 1
    if (val > 200)
        val := 200
    if (val != noteCount) {
        noteCount := val
        BuildMainGUI()
    }
return

NoteClick:
    GuiControlGet, hN, Hwnd, %A_GuiControl%
    i := noteHwndMap[hN]
    if (i = activeNote)
        return
    SetNoteColor(activeNote, noteColors[activeNote], false)
    activeNote := i
    SetNoteColor(i, noteColors[i], true)
return

WheelClick:
    global activeNote, wheelPicHWND, wheelSize, wheelRadius
    if (activeNote = 0)
        return
    GuiControlGet, wPos, Pos, %wheelPicHWND%
    cx := wPosX + wheelSize / 2.0
    cy := wPosY + wheelSize / 2.0
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
    color := (0xFF << 24) | (rgb[1] << 16) | (rgb[2] << 8) | rgb[3]
    SetNoteColor(activeNote, color, true)
return

GotoColorset:
    ShowSection(1)
return

GotoSettings:
    ShowSection(2)
return

BrowsePaths:
    global colorsetsPath, settingsFile
    FileSelectFolder, sel, %colorsetsPath%, 3, Select your colorsets folder
    if (sel != "") {
        colorsetsPath := sel
        GuiControl, , CsPathEdit, %sel%
        GuiControl, , StPathEdit, %sel%
        IniWrite, %sel%, %settingsFile%, Settings, colorsets_path
        GuiControl, , CsStatus, Colorsets path updated.
    }
return

SaveSettings:
    global colorsetsPath, settingsFile
    GuiControlGet, p2, , StPathEdit
    p2 := Trim(p2)
    if (p2 = "") {
        GuiControl, , StStatus, Please enter a colorsets path.
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
        GuiControl, , CsStatus, Please set a colorsets path first.
        return
    }
    if !FileExist(path)
        FileCreateDir, %path%
    if !FileExist(path) {
        GuiControl, , CsStatus, Could not create folder.
        return
    }
    if (nm = "") {
        FormatTime, ts, , yyyyMMdd_HHmmss
        nm := "colorset_" . ts
    }
    if !RegExMatch(nm, "i)\.txt$")
        nm := nm . ".txt"
    text := ""
    Loop, %noteCount% {
        color := noteColors[A_Index]
        r := (color >> 16) & 0xFF
        g := (color >> 8) & 0xFF
        b := color & 0xFF
        text .= Format("#{1:02X}{2:02X}{3:02X}", r, g, b) . "`r`n"
    }
    fullPath := path . "\" . nm
    FileAppend, %text%, %fullPath%
    GuiControl, , CsStatus, Saved to %fullPath%
return

GuiClose:
GuiEscape:
    global noteHbitmaps, wheelHBitmap, logoHBitmap
    for k, hb in noteHbitmaps
        DllCall("DeleteObject", "Ptr", hb)
    if (wheelHBitmap)
        DllCall("DeleteObject", "Ptr", wheelHBitmap)
    if (logoHBitmap)
        DllCall("DeleteObject", "Ptr", logoHBitmap)
    Gdip_Shutdown()
    ExitApp
return

HexColor(num) {
    return Format("{:06X}", num & 0xFFFFFF)
}

DefaultColor(i) {
    global noteCount
    hue := Mod((i - 1) * 360.0 / noteCount, 360)
    rgb := HsvToRgb(hue, 1.0, 1.0)
    return (0xFF << 24) | (rgb[1] << 16) | (rgb[2] << 8) | rgb[3]
}

SetNoteColor(i, color, active) {
    global noteColors, noteHwnds, noteHbitmaps
    noteColors[i] := color
    border := active ? 0xFFFFFFFF : 0xFF111318
    bmp := CreateColorBitmap(color, 40, 40, border)
    hb := Gdip_CreateHBITMAPFromBitmap(bmp)
    old := noteHbitmaps[i]
    ctrl := noteHwnds[i]
    if (ctrl)
        SetControlImage(ctrl, hb)
    if (old)
        DllCall("DeleteObject", "Ptr", old)
    noteHbitmaps[i] := hb
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
    Gdip_FillRectangle(gfx, brush, 2, 2, w - 4, h - 4)
    Gdip_DeleteBrush(brush)
    if (border) {
        pen := Gdip_CreatePen(border, 3)
        Gdip_DrawRectangle(gfx, pen, 2, 2, w - 4, h - 4)
        Gdip_DeletePen(pen)
    }
    Gdip_DeleteGraphics(gfx)
    return bmp
}

ATan2(y, x) {
    if (x > 0)
        return ATan(y / x)
    if (x < 0 && y >= 0)
        return ATan(y / x) + 3.141592653589793
    if (x < 0 && y < 0)
        return ATan(y / x) - 3.141592653589793
    if (x = 0 && y > 0)
        return 3.141592653589793 / 2
    if (x = 0 && y < 0)
        return -3.141592653589793 / 2
    return 0
}

HsvToRgb(h, s, v) {
    c := v * s
    x := c * (1 - Abs(Mod(h / 60.0, 2) - 1))
    m := v - c
    if (h < 60) {
        r := c, g := x, b := 0
    } else if (h < 120) {
        r := x, g := c, b := 0
    } else if (h < 180) {
        r := 0, g := c, b := x
    } else if (h < 240) {
        r := 0, g := x, b := c
    } else if (h < 300) {
        r := x, g := 0, b := c
    } else {
        r := c, g := 0, b := x
    }
    return [Round((r + m) * 255), Round((g + m) * 255), Round((b + m) * 255)]
}

CreateWheel(size) {
    bmp := Gdip_CreateBitmap(size, size)
    gfx := Gdip_GraphicsFromImage(bmp)
    Gdip_SetSmoothingMode(gfx, 4)
    center := size / 2.0
    radius := size / 2.0 - 3
    Loop, 360 {
        ang := (A_Index - 1) * 3.141592653589793 / 180.0
        x1 := center + Cos(ang) * 3
        y1 := center + Sin(ang) * 3
        x2 := center + Cos(ang) * radius
        y2 := center + Sin(ang) * radius
        rgb := HsvToRgb(A_Index - 1, 1.0, 1.0)
        color := (0xFF << 24) | (rgb[1] << 16) | (rgb[2] << 8) | rgb[3]
        pen := Gdip_CreatePen(color, 2)
        Gdip_DrawLine(gfx, pen, x1, y1, x2, y2)
        Gdip_DeletePen(pen)
    }
    Loop, % (radius + 1) {
        i := A_Index - 1
        r := radius - i
        alpha := Round((i / radius) * 255)
        brush := Gdip_CreateBrushSolid((alpha << 24) | 0xFFFFFF)
        Gdip_FillEllipse(gfx, brush, center - r, center - r, r * 2, r * 2)
        Gdip_DeleteBrush(brush)
    }
    pen := Gdip_CreatePen(0xFF3A4A5C, 2)
    Gdip_DrawEllipse(gfx, pen, 2, 2, size - 4, size - 4)
    Gdip_DeletePen(pen)
    brush := Gdip_CreateBrushSolid(0xFFFFFFFF)
    Gdip_FillEllipse(gfx, brush, center - 3, center - 3, 6, 6)
    Gdip_DeleteBrush(brush)
    Gdip_DeleteGraphics(gfx)
    return bmp
}

DrawLogo() {
    global accentBlue, accentPink, accentPurple, textColor
    bmp := Gdip_CreateBitmap(170, 70)
    gfx := Gdip_GraphicsFromImage(bmp)
    Gdip_SetSmoothingMode(gfx, 4)

    brush := Gdip_CreateBrushSolid(0x22000000)
    Gdip_FillEllipse(gfx, brush, -60, -20, 250, 130)
    Gdip_DeleteBrush(brush)

    pen := Gdip_CreatePen(accentBlue, 3)
    Gdip_DrawRoundedRectangle(gfx, pen, 8, 14, 34, 34, 8)
    Gdip_DeletePen(pen)

    p1 := Gdip_CreatePen(accentPurple, 4)
    p2 := Gdip_CreatePen(accentPink, 4)
    Gdip_DrawCurve(gfx, p1, [48,18, 66,30, 84,40])
    Gdip_DrawCurve(gfx, p2, [84,40, 100,30, 116,18])
    Gdip_DeletePen(p1)
    Gdip_DeletePen(p2)

    brush := Gdip_CreateBrushSolid(accentBlue)
    Gdip_FillRectangle(gfx, brush, 128, 10, 6, 6)
    Gdip_FillRectangle(gfx, brush, 118, 30, 8, 8)
    Gdip_FillRectangle(gfx, brush, 108, 16, 5, 5)
    Gdip_DeleteBrush(brush)

    Gdip_DrawString(gfx, "RhythKit", "Segoe UI", 15, textColor, 44, 26)

    Gdip_DeleteGraphics(gfx)
    hb := Gdip_CreateHBITMAPFromBitmap(bmp)
    Gdip_DisposeImage(bmp)
    return hb
}

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
    DllCall("gdiplus\GdiplusDeletePen", "Ptr", pen)
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