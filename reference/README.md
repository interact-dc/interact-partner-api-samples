# Reference reports

A fixed input for each kind of report, and the results it gives. Use them to check that your
integration sends what you mean and shows the right figures: put each input through your own
system, then compare what you get, and what your app shows, with the expected results here.

The expected results were made on **2026-11-10** on the staging sandbox. The same inputs give
exactly the same results, as long as the analysis model behind the API hasn't changed. If a
figure differs, check first whether we've told you of a model update since that date.

## The six reports

| File | Report | The estate | Compared against |
| --- | --- | --- | --- |
| `1-estate-refresh` | Estate Refresh | 22 servers over two generations: 10 Dell R640, 6 Dell R740, 4 HPE DL380 Gen10, 2 Dell R630 | Dell R660 and HPE DL380 Gen11, each with 2 Xeon Gold CPUs and 512 GB |
| `2-host-to-host` | Host-to-Host | The same 22 servers | The same two servers, 10 of each |
| `3-discovery` | Discovery | The same 22, plus 2 Dell R750xa GPU servers | Nothing: Discovery describes the estate as it is |
| `4-gpu-estate-refresh` | GPU Estate Refresh | 4 Dell R750xa, each with 4 NVIDIA A100 PCIe 80 GB | Dell R760xa with 4 NVIDIA H100 PCIe 80 GB, and with 4 NVIDIA L40S |
| `5-gpu-host-to-host` | GPU Host-to-Host | The same 4 R750xa | The H100 server (2 of them) and the L40S server (4 of them) |
| `6-compliance-label` | EU compliance report | The 22 CPU servers and the 2 GPU servers, with their drives | A UK data centre: PUE 1.4, WUE 1.2, 30% server utilisation, servers in Efficiency mode |

Every simulation uses the same assumptions: 30% server utilisation, 60% GPU utilisation, a PUE
of 1.5, electricity at £0.25 a kWh, maintenance at £1,200 a server and licensing at £50 a core,
in GBP. Every server is sent with its catalogue match, so none needs reviewing.

## How to use them

- **`inputs/`** holds each report exactly as it is sent to the API: the body of
  `POST /v1/tenants/{tenant}/simulations`, or of `.../compliance-reports` for the label. Send
  them through your own system, then start a run.
- **`expected/`** holds each run as the API returns it: its status, the credits it used and its
  `results`. Your results should match these exactly, apart from ids and times.
- **The sample can do it for you.** With your client's settings (as for the walkthrough), this
  runs all six in a tenant of yours, `interact-reference` unless you set `INTERACT_TENANT`, and
  lists any figure that differs:

  ```sh
  dotnet run --project dotnet/samples/Walkthrough -- reference
  ```

  Name files to run only those, e.g. `reference 1-estate-refresh`. A full set costs 10 credits,
  which it gives the tenant from your pool.

## The figures to expect

Simulations, per target:

| Report | Target | Servers | Energy, kWh a year | Energy saved | Yearly saving | Grade |
| --- | --- | --- | --- | --- | --- | --- |
| 1-estate-refresh | Dell PowerEdge R660 (r660) | 22 → 9 | 42,690 → 31,364 | 27% | £29,232 | B → A |
| 1-estate-refresh | HPE ProLiant DL380 Gen11 (dl380-gen11) | 22 → 8 | 42,690 → 32,372 | 24% | £33,380 | B → A |
| 2-host-to-host | Dell PowerEdge R660 (r660) | 22 → 10 | 42,690 → 31,934 | 25% | £24,689 | B → A |
| 2-host-to-host | HPE ProLiant DL380 Gen11 (dl380-gen11) | 22 → 10 | 42,690 → 34,887 | 18% | £23,951 | B → A |
| 4-gpu-estate-refresh | Dell PowerEdge R760xa (r760xa-h100) | 4 → 2 | 59,424 → 32,582 | 45% | £15,510 | A → A |
| 4-gpu-estate-refresh | Dell PowerEdge R760xa (r760xa-l40s) | 4 → 4 | 59,424 → 58,017 | 2% | £352 | A → A |
| 5-gpu-host-to-host | Dell PowerEdge R760xa (r760xa-h100) | 4 → 2 | 59,424 → 29,480 | 50% | £16,286 | A → A |
| 5-gpu-host-to-host | Dell PowerEdge R760xa (r760xa-l40s) | 4 → 4 | 59,424 → 53,835 | 9% | £1,397 | A → A |

Discovery: 24 servers, 920 cores and 9,472 GB of memory;
72,402 kWh a year, costing £18,100 in energy,
£28,800 in maintenance and £46,000 in licensing.
Servers by grade: A: 2, B: 16, C: 2, D: 4.

The EU compliance label: PUE 1.4 (grade D), WUE 1.2 (grade G);
95,433 kWh a year in total (59,305 kWh by the servers);
7,769 kgCO2e scope 2 and 48,251 kgCO2e scope 3; 240 m³ of water.
All 24 servers rated.
