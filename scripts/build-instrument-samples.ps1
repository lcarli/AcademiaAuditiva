#requires -Version 7.0
<#
.SYNOPSIS
    Rebuilds the guitar and violin samples bundled with the app.

.DESCRIPTION
    Unlike the piano (private `piano-audio` blob container), the other
    instruments ship in the repository under
    AcademiaAuditiva/Audio/Instruments/<folder>/ and the mixer reads them
    from disk. Their source is the FluidR3_GM SoundFont as rendered to MP3 by
    gleitz/midi-js-soundfonts (see Audio/Instruments/LICENSE.txt), pinned to
    one commit so a rebuild is reproducible.

    For every semitone from C1 to B7 (the piano's 84 notes) the script:

      1. downloads the source MP3 (named with flats, e.g. Db4.mp3) and mixes
         it down to mono, like the piano samples;
      2. replaces a source note that is silent (the violin render has no
         sound for Bb6 and Gb7..B7) with the nearest sounding note of the
         same instrument, resampled to the missing pitch;
      3. sets the loudness of the first 1.5 s (the usual clip length) to the
         piano samples' median, -18.4 LUFS, without letting the true peak
         go above -3 dBTP, the level the piano samples are normalised to;
      4. encodes it as 44.1 kHz mono MP3 named with the piano's sharp
         spelling (Cs4.mp3).

    The samples are committed, so this only needs to run again to change
    them. Requires ffmpeg (with libmp3lame) on PATH.

.PARAMETER Instrument
    Folders to rebuild. Defaults to every bundled instrument.

.PARAMETER OutDir
    Root of the bundled samples. Defaults to AcademiaAuditiva/Audio/Instruments.

.EXAMPLE
    ./scripts/build-instrument-samples.ps1 -Instrument violin
#>
[CmdletBinding()]
param(
    [ValidateSet('guitar', 'violin')]
    [string[]]$Instrument = @('guitar', 'violin'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\AcademiaAuditiva\Audio\Instruments')
)

$ErrorActionPreference = 'Stop'

$SourceCommit = '044fab8e1456bfafc5776e86dfd6bb8697149aef'
$SourceFolders = @{ guitar = 'acoustic_guitar_nylon-mp3'; violin = 'violin-mp3' }
$TargetLufs = -18.4
$MaxTruePeak = -3.0
$MeasureSeconds = 1.5
$SilentBelowLufs = -60.0
$Bitrate = '96k'
$Mono = 'pan=mono|c0=0.5*c0+0.5*c1'

$Sharps = 'C', 'Cs', 'D', 'Ds', 'E', 'F', 'Fs', 'G', 'Gs', 'A', 'As', 'B'
$Flats = 'C', 'Db', 'D', 'Eb', 'E', 'F', 'Gb', 'G', 'Ab', 'A', 'Bb', 'B'
$Invariant = [Globalization.CultureInfo]::InvariantCulture

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    throw 'ffmpeg was not found on PATH.'
}

# Returns the last value ffmpeg's ebur128 summary printed for a field
# (-inf, printed for silence, becomes negative infinity).
function Read-Ebur128([string]$Log, [string]$Field, [string]$Unit) {
    $match = [regex]::Matches($Log, "${Field}:\s+(-inf|-?[\d.]+) $Unit")
    if ($match.Count -eq 0) { throw "ffmpeg did not report $Field" }
    $value = $match[$match.Count - 1].Groups[1].Value
    if ($value -eq '-inf') { return [double]::NegativeInfinity }
    [double]::Parse($value, $Invariant)
}

# Loudness of the first $MeasureSeconds and true peak of the whole file,
# after the optional filter.
function Measure-Loudness([string]$Path, [string]$Filter = 'anull') {
    $start = & ffmpeg -hide_banner -nostats -t $MeasureSeconds -i $Path -af "$Filter,ebur128" -f null - 2>&1 | Out-String
    $whole = & ffmpeg -hide_banner -nostats -i $Path -af "$Filter,ebur128=peak=true" -f null - 2>&1 | Out-String
    [pscustomobject]@{
        Lufs     = Read-Ebur128 $start 'I' 'LUFS'
        TruePeak = Read-Ebur128 $whole 'Peak' 'dBFS'
    }
}

function Invoke-Ffmpeg([string[]]$Arguments) {
    & ffmpeg -hide_banner -loglevel error -y @Arguments
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg $($Arguments -join ' ') failed" }
}

$work = Join-Path ([IO.Path]::GetTempPath()) "aa-instrument-samples-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
try {
    foreach ($name in $Instrument) {
        $target = Join-Path $OutDir $name
        New-Item -ItemType Directory -Force $target | Out-Null
        Write-Host "Building $name samples in $target"

        # Index i is MIDI note 24 + i (C1 .. B7).
        $notes = foreach ($octave in 1..7) {
            for ($pc = 0; $pc -lt 12; $pc++) {
                $download = Join-Path $work "$name-$($Flats[$pc])$octave.mp3"
                $url = "https://raw.githubusercontent.com/gleitz/midi-js-soundfonts/$SourceCommit/FluidR3_GM/$($SourceFolders[$name])/$($Flats[$pc])$octave.mp3"
                Invoke-WebRequest -Uri $url -OutFile $download -UseBasicParsing -MaximumRetryCount 3 -RetryIntervalSec 2
                $level = Measure-Loudness $download $Mono
                [pscustomobject]@{
                    File     = "$($Sharps[$pc])$octave.mp3"
                    Source   = $download
                    Sounding = $level.Lufs -gt $SilentBelowLufs
                }
            }
        }

        for ($i = 0; $i -lt $notes.Count; $i++) {
            # The nearest sounding note, the lower one on a tie.
            $donor = 0..($notes.Count - 1) |
                Where-Object { $notes[$_].Sounding } |
                Sort-Object { [Math]::Abs($_ - $i) }, { $_ } |
                Select-Object -First 1
            if ($null -eq $donor) { throw "Every $name source note is silent." }

            $shift = $i - $donor
            $filter = if ($shift -eq 0) { $Mono } else {
                [string]::Format($Invariant, '{0},asetrate={1:F4},aresample=44100', $Mono, 44100 * [Math]::Pow(2, $shift / 12.0))
            }
            $wav = Join-Path $work "$name-$($notes[$i].File).wav"
            Invoke-Ffmpeg @('-i', $notes[$donor].Source, '-af', $filter, '-ac', '1', '-ar', '44100', '-c:a', 'pcm_s16le', $wav)

            $level = Measure-Loudness $wav
            $gain = [Math]::Min($TargetLufs - $level.Lufs, $MaxTruePeak - $level.TruePeak)
            Invoke-Ffmpeg @('-i', $wav, '-af', [string]::Format($Invariant, 'volume={0:F2}dB', $gain),
                '-c:a', 'libmp3lame', '-b:a', $Bitrate, '-map_metadata', '-1', '-id3v2_version', '0',
                (Join-Path $target $notes[$i].File))

            $from = if ($shift -eq 0) { '' } else { "  (from $($notes[$donor].File), $('{0:+0;-0}' -f $shift) st)" }
            Write-Host ([string]::Format($Invariant, '  {0,-7} {1,6:F1} LUFS {2,5:F1} dBTP  gain {3,5:F1} dB{4}',
                    $notes[$i].File, $level.Lufs, $level.TruePeak, $gain, $from))
        }
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
