# C:\GSADUs\Dev\Tools\AutoPull.ps1
$repo = "C:\GSADUs\Dev"
git -C $repo fetch --all
git -C $repo pull --rebase --autostash
