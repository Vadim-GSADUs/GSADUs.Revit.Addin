$repo = "C:\GSADUs\Dev"
git -C $repo fetch --all
git -C $repo pull --autostash
git -C $repo push
git -C $repo status -sb
shutdown /s /t 900 /c "Shutdown in 15 minutes. Use 'shutdown /a' to abort."
