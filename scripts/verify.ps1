$ErrorActionPreference = 'Stop'
$march = Invoke-RestMethod 'http://localhost:8080/api/reports/projects?year=2026&month=3'
$february = Invoke-RestMethod 'http://localhost:8080/api/reports/projects?year=2026&month=2'
$expected = @(@{Id='p001';Hours=12;Amount=7600;Percent=38},@{Id='p002';Hours=10;Amount=7000;Percent=140})
foreach ($row in $expected) {
  $actual = $march.items | Where-Object projectId -eq $row.Id
  if (-not $actual -or $actual.hours -ne $row.Hours -or $actual.amount -ne $row.Amount -or $actual.percent -ne $row.Percent) { throw "March acceptance mismatch: $($row.Id)" }
}
if ($march.totalHours -ne 22 -or $march.totalAmount -ne 14600) { throw 'March total mismatch' }
if ($february.totalHours -ne 8 -or $february.totalAmount -ne 4000) { throw 'February total mismatch' }
$march.items | Format-Table projectCode,hours,amount,budget,percent,isOverspent
"March total: $($march.totalHours) h / $($march.totalAmount) RUB"
"February total: $($february.totalHours) h / $($february.totalAmount) RUB"
"ACCEPTANCE: PASSED"
