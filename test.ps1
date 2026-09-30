param([string]$TestDirectory = (Join-Path $env:TEMP ('RenamerTests-' + [guid]::NewGuid().ToString('N'))), [string]$Screenshot, [string]$ExecutablePath = (Join-Path $PSScriptRoot 'PLrename.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$null = [Reflection.Assembly]::LoadFrom($ExecutablePath)
$null = New-Item -ItemType Directory -Path $TestDirectory -Force
$script:passed = 0
function AssertEqual($actual, $expected) {
    if ($actual -cne $expected) { throw "Expected '$expected', got '$actual'" }
    $script:passed++
}
function AssertThrows([scriptblock]$action) {
    $threw = $false
    try { & $action } catch { $threw = $true }
    if (-not $threw) { throw 'Expected operation to be blocked.' }
    $script:passed++
}
AssertEqual ([BatchNumberRenamer.Engine]::Sequence('a.jpg','0101',1,1,'','')) '0102.jpg'
AssertEqual ([BatchNumberRenamer.Engine]::Sequence('a.jpg','009',1,1,'','')) '010.jpg'
AssertEqual ([BatchNumberRenamer.Engine]::Sequence('a.jpg','999',1,1,'IMG_','_copy')) 'IMG_1000_copy.jpg'
AssertEqual ([BatchNumberRenamer.Engine]::Sequence('a.jpg','010',-1,1,'','')) '009.jpg'
AssertEqual ([BatchNumberRenamer.Engine]::Shift('photo_009.jpg',1)) 'photo_010.jpg'
AssertEqual ([BatchNumberRenamer.Engine]::Shift('0101.png',1)) '0102.png'
AssertEqual ([BatchNumberRenamer.Engine]::Shift('set2_photo009_edit.jpg',1)) 'set2_photo010_edit.jpg'
AssertEqual ([BatchNumberRenamer.Engine]::Shift('009',1)) '010'
AssertThrows { [BatchNumberRenamer.Engine]::Shift('photo.jpg',1) }
AssertThrows { [BatchNumberRenamer.Engine]::Shift('000.jpg',-1) }
AssertThrows { [BatchNumberRenamer.Engine]::Shift('9223372036854775807.jpg',1) }
AssertThrows { [BatchNumberRenamer.Engine]::Sequence('a.jpg','abc',1,0,'','') }
$a = Join-Path $TestDirectory '009.txt'
$b = Join-Path $TestDirectory '010.txt'
$c = Join-Path $TestDirectory '011.txt'
[IO.File]::WriteAllText($a,'first-content')
[IO.File]::WriteAllText($b,'second-content')
$plan = [Collections.Generic.List[BatchNumberRenamer.Rename]]::new()
$plan.Add([BatchNumberRenamer.Rename]::new($a,$b))
$plan.Add([BatchNumberRenamer.Rename]::new($b,$c))
$journalDir = Join-Path $TestDirectory 'journals'
$journal = [BatchNumberRenamer.Engine]::Apply($plan,$journalDir)
AssertEqual ([IO.File]::ReadAllText($b)) 'first-content'
AssertEqual ([IO.File]::ReadAllText($c)) 'second-content'
AssertEqual ([IO.File]::Exists($a)) $false
AssertEqual ([IO.File]::Exists($journal)) $true
$reverse = [Collections.Generic.List[BatchNumberRenamer.Rename]]::new()
$reverse.Add([BatchNumberRenamer.Rename]::new($b,$a))
$reverse.Add([BatchNumberRenamer.Rename]::new($c,$b))
$null = [BatchNumberRenamer.Engine]::Apply($reverse,$journalDir)
AssertEqual ([IO.File]::ReadAllText($a)) 'first-content'
AssertEqual ([IO.File]::ReadAllText($b)) 'second-content'
$swap = [Collections.Generic.List[BatchNumberRenamer.Rename]]::new()
$swap.Add([BatchNumberRenamer.Rename]::new($a,$b))
$swap.Add([BatchNumberRenamer.Rename]::new($b,$a))
$null = [BatchNumberRenamer.Engine]::Apply($swap,$journalDir)
AssertEqual ([IO.File]::ReadAllText($a)) 'second-content'
AssertEqual ([IO.File]::ReadAllText($b)) 'first-content'
$bad = [Collections.Generic.List[BatchNumberRenamer.Rename]]::new()
$bad.Add([BatchNumberRenamer.Rename]::new($a,$b))
AssertThrows { [BatchNumberRenamer.Engine]::Apply($bad,$journalDir) }
AssertEqual ([IO.File]::ReadAllText($a)) 'second-content'
AssertEqual ([IO.File]::ReadAllText($b)) 'first-content'
$bad.Clear(); $bad.Add([BatchNumberRenamer.Rename]::new($a,(Join-Path $TestDirectory 'CON.txt')))
AssertThrows { [BatchNumberRenamer.Engine]::Validate($bad) }
$bad.Clear(); $bad.Add([BatchNumberRenamer.Rename]::new($a,$c)); $bad.Add([BatchNumberRenamer.Rename]::new($b,$c))
AssertThrows { [BatchNumberRenamer.Engine]::Validate($bad) }
$bad.Clear(); $bad.Add([BatchNumberRenamer.Rename]::new($a,(Join-Path $TestDirectory 'bad?.txt')))
AssertThrows { [BatchNumberRenamer.Engine]::Validate($bad) }

# Exercise a filesystem failure after staging. Holding the second source open
# forces rollback of the already staged first source.
$locked = [IO.File]::Open($b,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try { AssertThrows { [BatchNumberRenamer.Engine]::Apply($plan,$journalDir) } } finally { $locked.Dispose() }
AssertEqual ([IO.File]::ReadAllText($a)) 'second-content'
AssertEqual ([IO.File]::ReadAllText($b)) 'first-content'
AssertEqual ((Get-ChildItem -LiteralPath $TestDirectory -Filter '.rename-*').Count) 0

[Windows.Forms.Application]::EnableVisualStyles()
$form = [BatchNumberRenamer.MainWindow]::new()
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$box = $form.GetType().GetField('dropBox',$flags).GetValue($form)
$grid = $form.GetType().GetField('grid',$flags).GetValue($form)
AssertEqual ($null -ne $form.Icon) $true
AssertEqual $box.AllowDrop $true
$data = [Windows.Forms.DataObject]::new()
$data.SetData([Windows.Forms.DataFormats]::FileDrop,[string[]]@($a))
$drag = [Windows.Forms.DragEventArgs]::new($data,0,0,0,[Windows.Forms.DragDropEffects]::Copy,[Windows.Forms.DragDropEffects]::None)
$enter = [Windows.Forms.Control].GetMethod('OnDragEnter',$flags)
$drop = [Windows.Forms.Control].GetMethod('OnDragDrop',$flags)
$null = $enter.Invoke($box,[object[]]@($drag))
AssertEqual $drag.Effect ([Windows.Forms.DragDropEffects]::Copy)
AssertEqual $box.DragActive $true
$null = $drop.Invoke($box,[object[]]@($drag))
AssertEqual $box.DragActive $false
AssertEqual $grid.Rows.Count 1
AssertEqual $grid.Rows.Count 1
AssertEqual $grid.Rows[0].Cells[1].Value '0001.txt'
$data.SetData([Windows.Forms.DataFormats]::FileDrop,[string[]]@($TestDirectory))
$null = $drop.Invoke($box,[object[]]@($drag))
AssertEqual $grid.Rows.Count 2
$textData = [Windows.Forms.DataObject]::new('text only')
$invalidDrag = [Windows.Forms.DragEventArgs]::new($textData,0,0,0,[Windows.Forms.DragDropEffects]::Copy,[Windows.Forms.DragDropEffects]::Copy)
$null = $enter.Invoke($box,[object[]]@($invalidDrag))
AssertEqual $invalidDrag.Effect ([Windows.Forms.DragDropEffects]::None)
AssertEqual $box.DragActive $false
$sort = $form.GetType().GetField('sort',$flags).GetValue($form)
$newer = [DateTime]::SpecifyKind([DateTime]'2026-09-20T12:00:00',[DateTimeKind]::Utc)
$older = $newer.AddDays(-1)
[IO.File]::SetLastWriteTimeUtc($a,$newer)
[IO.File]::SetLastWriteTimeUtc($b,$older)
$sort.SelectedIndex = 1
AssertEqual $grid.Rows[0].Cells[0].Value '010.txt'
AssertEqual $grid.Rows[0].Cells[1].Value '0001.txt'
AssertEqual $grid.Rows[1].Cells[0].Value '009.txt'
$sort.SelectedIndex = 2
AssertEqual $grid.Rows[0].Cells[0].Value '009.txt'
AssertEqual $grid.Rows[0].Cells[2].Value ($newer.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss'))
[IO.File]::SetLastWriteTimeUtc($b,$newer)
$sort.SelectedIndex = 1
AssertEqual $grid.Rows[0].Cells[0].Value '009.txt'
[IO.File]::SetLastWriteTimeUtc($b,$older)
$sort.SelectedIndex = 2

# Run an actual asynchronous batch and undo while pumping the native UI queue.
$form.Show(); [Windows.Forms.Application]::DoEvents()
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Forms.WindowsFormsSynchronizationContext]::new())
$run = $form.GetType().GetMethod('RunBatch',$flags)
$batch = $form.GetType().GetField('plan',$flags).GetValue($form)
$batchCopy = [Collections.Generic.List[BatchNumberRenamer.Rename]]::new($batch)
$null = $run.Invoke($form,[object[]]@($batchCopy,$false,[string]$journalDir))
function WaitBatch {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($form.GetType().GetField('busy',$flags).GetValue($form)) {
        [Windows.Forms.Application]::DoEvents()
        if ($timer.Elapsed.TotalSeconds -gt 10) { throw 'Asynchronous batch timed out.' }
        Start-Sleep -Milliseconds 10
    }
    [Windows.Forms.Application]::DoEvents()
}
WaitBatch
$progress = $form.GetType().GetField('progressBar',$flags).GetValue($form)
AssertEqual $progress.Value 100
AssertEqual $sort.Enabled $true
AssertEqual ([IO.File]::ReadAllText((Join-Path $TestDirectory '0001.txt'))) 'second-content'
AssertEqual ([IO.File]::ReadAllText((Join-Path $TestDirectory '0002.txt'))) 'first-content'
$undoBatch = [Collections.Generic.List[BatchNumberRenamer.Rename]]::new()
foreach ($r in $batchCopy) { $undoBatch.Add([BatchNumberRenamer.Rename]::new($r.To,$r.From)) }
$null = $run.Invoke($form,[object[]]@($undoBatch,$true,[string]$journalDir))
WaitBatch
AssertEqual $progress.Value 100
AssertEqual ([IO.File]::ReadAllText($a)) 'second-content'
AssertEqual ([IO.File]::ReadAllText($b)) 'first-content'
AssertEqual ($null -eq $form.GetType().GetField('last',$flags).GetValue($form)) $true
$status = $form.GetType().GetField('status',$flags).GetValue($form)
AssertEqual $status.Text 'Original names restored.'
AssertEqual $status.ForeColor.ToArgb() ([Drawing.Color]::FromArgb(65,80,95).ToArgb())
$sessionLogs = @(Get-ChildItem -LiteralPath $journalDir -Filter '*.tsv')
AssertEqual ($sessionLogs.Count -gt 0) $true
$unrelatedLog = Join-Path $journalDir 'unrelated.tsv'
[IO.File]::WriteAllText($unrelatedLog,'keep this file')
$temporaryJournal = $sessionLogs[0].FullName + '.new'
[IO.File]::WriteAllText($temporaryJournal,'temporary journal')
if ($Screenshot) {
    $form.Show()
    $form.Refresh()
    Start-Sleep -Milliseconds 350
    [Windows.Forms.Application]::DoEvents()
    $bitmap = [Drawing.Bitmap]::new($form.Width,$form.Height)
    $form.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,$form.Width,$form.Height))
    $bitmap.Save($Screenshot,[Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}
$form.Close()
foreach ($log in $sessionLogs) { AssertEqual ([IO.File]::Exists($log.FullName)) $false }
AssertEqual ([IO.File]::Exists($temporaryJournal)) $false
AssertEqual ([IO.File]::ReadAllText($unrelatedLog)) 'keep this file'
AssertEqual ([IO.File]::ReadAllText($a)) 'second-content'
AssertEqual ([IO.File]::ReadAllText($b)) 'first-content'
$form.Dispose()
Write-Output "$script:passed checks passed. Test files: $TestDirectory"
