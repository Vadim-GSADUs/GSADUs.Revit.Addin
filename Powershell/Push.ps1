$repo = "C:\GSADUs\Dev"
git -C $repo fetch --all
git -C $repo pull --autostash
git -C $repo push
git -C $repo status -sb
