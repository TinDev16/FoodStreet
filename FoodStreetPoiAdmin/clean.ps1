$c = Get-Content 'Program.cs'
$clean = $c[0..1755] + $c[1969..($c.Length-1)]
$clean | Set-Content 'Program.cs_temp'
