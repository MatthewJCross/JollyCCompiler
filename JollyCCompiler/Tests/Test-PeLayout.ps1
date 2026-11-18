param([Parameter(Mandatory=$true)][string]$Path)
$ErrorActionPreference = 'Stop'
$bytes = [IO.File]::ReadAllBytes((Resolve-Path $Path))
function U16([int]$offset) { [BitConverter]::ToUInt16($bytes, $offset) }
function U32([int]$offset) { [BitConverter]::ToUInt32($bytes, $offset) }
if ($bytes.Length -lt 512 -or (U16 0) -ne 0x5A4D) { throw 'Not an MZ executable' }
$pe = [int](U32 0x3C)
if ($pe -lt 0 -or $pe + 24 -gt $bytes.Length -or (U32 $pe) -ne 0x00004550) { throw 'Invalid PE signature' }
if ((U16 ($pe + 4)) -ne 0x8664) { throw 'Not AMD64' }
$sections = [int](U16 ($pe + 6))
$opt = $pe + 24
if ((U16 $opt) -ne 0x20B) { throw 'Not PE32+' }
$optSize = [int](U16 ($pe + 20))
$headers = $opt + $optSize
$sizeOfImage = U32 ($opt + 56)
$sectionAlignment = U32 ($opt + 32)
if ($sectionAlignment -eq 0 -or $sections -lt 1) { throw 'Invalid section metadata' }
$items = @()
for ($i=0; $i -lt $sections; $i++) {
  $s = $headers + $i*40
  if ($s + 40 -gt $bytes.Length) { throw 'Section header outside file' }
  $name = [Text.Encoding]::ASCII.GetString($bytes, $s, 8).Trim([char]0)
  $vsize = U32 ($s+8); $rva = U32 ($s+12)
  $rawSize = U32 ($s+16); $rawPtr = U32 ($s+20)
  if ([long]$rawPtr + $rawSize -gt $bytes.Length) { throw "Raw section $name exceeds file" }
  if ($rva % $sectionAlignment -ne 0) { throw "Unaligned RVA: $name" }
  $extent = [math]::Max([long]$vsize,[long]$rawSize)
  $aligned = [long][math]::Ceiling($extent / $sectionAlignment) * $sectionAlignment
  $items += [pscustomobject]@{Name=$name; RVA=[long]$rva; End=[long]$rva+$aligned; RawSize=$rawSize}
}
$sorted = @($items | Sort-Object RVA)
for ($i=1; $i -lt $sorted.Count; $i++) {
  if ($sorted[$i].RVA -lt $sorted[$i-1].End) { throw "Sections overlap: $($sorted[$i-1].Name), $($sorted[$i].Name)" }
}
if ($sorted[-1].End -gt $sizeOfImage) { throw 'SizeOfImage does not cover sections' }
$names = @($items | ForEach-Object Name)
foreach ($required in @('.text','.rdata','.idata')) {
  if ($required -notin $names) { throw "Missing section $required" }
}
$items | Format-Table -AutoSize
Write-Host 'PASS: PE32+ AMD64 section layout is consistent.'
