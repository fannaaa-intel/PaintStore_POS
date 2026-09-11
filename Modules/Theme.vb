Imports System.Drawing

' =============================================================================
'  Theme  —  the single source of truth for colour and type.
'
'  WHY THIS EXISTS
'  Every colour in this app used to be written as raw channel numbers at the
'  point of use — Color.FromArgb(0, 168, 142) appeared over 1,300 times across
'  ~50 files, and the same palette was re-declared privately in 36 of them.
'  Changing the brand green meant finding and editing every one of those, and
'  that had already gone wrong: MintPrimary shipped as BOTH (0, 168, 142) in 36
'  files and (0, 200, 170) in six others, so two different greens were on
'  screen at once.
'
'  Everything below is declared once. Change a value here and the whole app
'  follows.
'
'  HOW TO USE IT
'      btn.BackColor = Theme.Primary
'      lbl.Font      = Theme.FontBody
'      lbl.Font      = Theme.Semibold(11)      ' when a size has no named slot
'
'  The names match what the panel builders already called these colours
'  (MintPrimary, MintDark, SubText, ...), so existing code reads the same way
'  after the raw numbers are swapped out.
'
'  Fonts are cached rather than constructed per control: `New Font(...)` was
'  being called over 1,200 times, and each one allocates a GDI handle.
' =============================================================================
Public Module Theme

    ' ── Brand greens ─────────────────────────────────────────────────────────
    ' Primary is the canonical brand green. PanelRenderer used to carry its own
    ' brighter (0, 200, 170) variant, which is the drift this module removes.
    Public ReadOnly Primary As Color = Color.FromArgb(0, 168, 142)      ' MintPrimary
    Public ReadOnly Dark As Color = Color.FromArgb(10, 79, 66)          ' MintDark — headers, titles
    Public ReadOnly Deep As Color = Color.FromArgb(0, 130, 109)         ' MintDeep — positive figures
    Public ReadOnly Forest As Color = Color.FromArgb(0, 120, 92)        ' deepest green accent

    ' ── Text ─────────────────────────────────────────────────────────────────
    Public ReadOnly TextDark As Color = Color.FromArgb(45, 45, 45)      ' body copy
    Public ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)     ' secondary copy
    Public ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)  ' hints, placeholders
    Public ReadOnly DeepInk As Color = Color.FromArgb(35, 50, 46)       ' high-contrast headings

    ' ── Surfaces ─────────────────────────────────────────────────────────────
    Public ReadOnly CardBg As Color = Color.White
    Public ReadOnly PageBg As Color = Color.FromArgb(244, 248, 246)     ' MintBg — page behind cards
    Public ReadOnly FieldBg As Color = Color.FromArgb(243, 246, 244)    ' input backgrounds
    Public ReadOnly TileBg As Color = Color.FromArgb(236, 248, 244)     ' soft mint tile
    Public ReadOnly Wash As Color = Color.FromArgb(210, 240, 235)       ' button hover wash
    Public ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)   ' grid row under the cursor

    ' ── Lines ────────────────────────────────────────────────────────────────
    Public ReadOnly Border As Color = Color.FromArgb(220, 235, 232)     ' the most-used line colour
    Public ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240) ' between grid rows
    Public ReadOnly BorderSoft As Color = Color.FromArgb(228, 232, 230)
    Public ReadOnly BorderMuted As Color = Color.FromArgb(210, 225, 220)

    ' ── Status accents ───────────────────────────────────────────────────────
    Public ReadOnly Danger As Color = Color.FromArgb(210, 60, 60)       ' AccentRed — errors, losses
    Public ReadOnly DangerBg As Color = Color.FromArgb(251, 234, 234)   ' behind a danger message
    Public ReadOnly Warning As Color = Color.FromArgb(232, 120, 30)     ' AccentOrange
    Public ReadOnly WarningBg As Color = Color.FromArgb(252, 240, 228)
    Public ReadOnly Accent As Color = Color.FromArgb(45, 80, 150)         ' AccentBlue
    Public ReadOnly Success As Color = Color.FromArgb(0, 160, 106)
    Public ReadOnly SuccessBg As Color = Color.FromArgb(200, 240, 225)

    ' ── Type ─────────────────────────────────────────────────────────────────
    Public Const FamilyName As String = "Segoe UI"
    Public Const FamilySemibold As String = "Segoe UI Semibold"

    ' The sizes that actually appear in the codebase, commonest first.
    Public ReadOnly FontBody As Font = New Font(FamilyName, 9.5F)          ' default control text
    Public ReadOnly FontSmall As Font = New Font(FamilyName, 8.5F)         ' hints, captions
    Public ReadOnly FontTiny As Font = New Font(FamilyName, 7.5F)          ' dense badges
    Public ReadOnly FontMedium As Font = New Font(FamilyName, 10.0F)
    Public ReadOnly FontLarge As Font = New Font(FamilyName, 11.0F)
    Public ReadOnly FontHeading As Font = New Font(FamilyName, 14.0F)
    Public ReadOnly FontDisplay As Font = New Font(FamilyName, 26.0F)      ' KPI figures

    Public ReadOnly FontBodyBold As Font = New Font(FamilySemibold, 9.5F)
    Public ReadOnly FontMediumBold As Font = New Font(FamilySemibold, 10.0F)
    Public ReadOnly FontLargeBold As Font = New Font(FamilySemibold, 11.0F)
    Public ReadOnly FontHeadingBold As Font = New Font(FamilySemibold, 14.0F)

    ' Escape hatches for the sizes that have no named slot. Both cache, so a
    ' loop building 200 rows does not allocate 200 identical Font handles.
    Private ReadOnly _regular As New Dictionary(Of Single, Font)
    Private ReadOnly _semibold As New Dictionary(Of Single, Font)

    Public Function Regular(size As Single) As Font
        SyncLock _regular
            Dim f As Font = Nothing
            If _regular.TryGetValue(size, f) Then Return f
            f = New Font(FamilyName, size)
            _regular(size) = f
            Return f
        End SyncLock
    End Function

    Public Function Semibold(size As Single) As Font
        SyncLock _semibold
            Dim f As Font = Nothing
            If _semibold.TryGetValue(size, f) Then Return f
            f = New Font(FamilySemibold, size)
            _semibold(size) = f
            Return f
        End SyncLock
    End Function

End Module
