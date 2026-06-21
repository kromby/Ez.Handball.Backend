# HBStatz scraping spike

Disposable proof-of-concept for Backend #7. Scrapes Olís deild karla outfield +
goalkeeper stats from hbstatz.is into JSON/CSV. NOT part of Ez.Handball.sln and
never wired into the ingestion pipeline. See FINDINGS.md for results.

Run: `dotnet run --project tools/HbStatz.Spike`
Output: `tools/HbStatz.Spike/output/*.json` and `*.csv`
