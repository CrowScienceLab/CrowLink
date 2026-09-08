param([string]$AssetDirectory = (Join-Path $PSScriptRoot '..\src\CrowLink.App\Assets'))
Add-Type -AssemblyName PresentationCore, WindowsBase
New-Item -ItemType Directory -Path $AssetDirectory -Force | Out-Null
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($size/256.0,$size/256.0))
    $background = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#080C12')
    $white = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#EDF8FF')
    $cyan = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#59D8EC')
    $drawing.DrawRoundedRectangle($background,$null,[System.Windows.Rect]::new(0,0,256,256),48,48)
    $bird = [System.Windows.Media.Geometry]::Parse('M30,156 C49,108 78,62 120,60 C134,33 169,37 181,62 L221,77 L179,87 C171,114 166,127 148,141 L122,157 L67,181 L87,145 L30,171 Z M68,130 C85,91 115,75 140,81 C118,103 103,119 68,130 Z')
    $drawing.DrawGeometry($white,$null,$bird)
    $drawing.DrawEllipse($background,$null,[System.Windows.Point]::new(158,65),5,5)
    $chain = [System.Windows.Media.Geometry]::Parse('M123,173 L144,152 C157,139 178,139 188,151 C199,163 195,178 185,188 L176,197 M149,186 L168,168 M157,180 L137,201 C124,214 104,214 94,202 C84,190 88,175 99,164 L107,156')
    $pen = [System.Windows.Media.Pen]::new($cyan,13)
    $pen.StartLineCap = $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $drawing.DrawGeometry($null,$pen,$chain)
    $drawing.Pop(); $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.MemoryStream]::new(); $encoder.Save($stream)
    $frames += ,($stream.ToArray()); $stream.Dispose()
}
[System.IO.File]::WriteAllBytes((Join-Path $AssetDirectory 'CrowLink.png'),$frames[-1])
$output = [System.IO.File]::Create((Join-Path $AssetDirectory 'CrowLink.ico'))
$writer = [System.IO.BinaryWriter]::new($output)
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
$offset = 6 + 16*$frames.Count
$sizes = @(16,24,32,48,64,128,256)
for($i=0;$i -lt $frames.Count;$i++) {
    $dimension = if($sizes[$i] -eq 256){0}else{$sizes[$i]}
    $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([uint16]0)
    $writer.Write([uint16]1);$writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach($frame in $frames){$writer.Write([byte[]]$frame)}
$writer.Dispose()
