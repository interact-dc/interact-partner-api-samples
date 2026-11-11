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
| `6-compliance-report` | EU compliance report | The 22 CPU servers and the 2 GPU servers, with their drives | A UK data centre: PUE 1.4, WUE 1.2, 30% server utilisation, servers in Efficiency mode |

Every simulation uses the same assumptions: 30% server utilisation, 60% GPU utilisation, a PUE
of 1.5, electricity at £0.25 a kWh, maintenance at £1,200 a server and licensing at £50 a core,
in GBP. Every server is sent with its catalogue match, so none needs reviewing.

**The GPU reports need GPU support.** Reports 4 and 5 are GPU simulations, and reports 3 and 6
include 2 GPU servers. The sample switches GPU support on for its tenant if your plan includes
it. If it doesn't, the sample leaves 4 and 5 out, and 3 and 6 are run with those servers taken
without their GPUs (the API says so in `warnings`), so their figures won't match the expected
ones.

## How to use them

- **`inputs/`** holds each report exactly as it is sent to the API: the body of
  `POST /v1/tenants/{tenant}/simulations`, or of `.../compliance-reports` for the compliance report. Send
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
| 1-estate-refresh | Dell PowerEdge R660 (r660) | 22 → 9 | 42,690 → 31,364 | 27% | £29,231.50 | B → A |
| 1-estate-refresh | HPE ProLiant DL380 Gen11 (dl380-gen11) | 22 → 8 | 42,690 → 32,372 | 24% | £33,379.50 | B → A |
| 2-host-to-host | Dell PowerEdge R660 (r660) | 22 → 10 | 42,690 → 31,934 | 25% | £24,689.00 | B → A |
| 2-host-to-host | HPE ProLiant DL380 Gen11 (dl380-gen11) | 22 → 10 | 42,690 → 34,887 | 18% | £23,950.75 | B → A |
| 4-gpu-estate-refresh | Dell PowerEdge R760xa (r760xa-h100) | 4 → 2 | 59,424 → 32,582 | 45% | £15,510.50 | A → A |
| 4-gpu-estate-refresh | Dell PowerEdge R760xa (r760xa-l40s) | 4 → 4 | 59,424 → 58,017 | 2% | £351.75 | A → A |
| 5-gpu-host-to-host | Dell PowerEdge R760xa (r760xa-h100) | 4 → 2 | 59,424 → 29,480 | 50% | £16,286.00 | A → A |
| 5-gpu-host-to-host | Dell PowerEdge R760xa (r760xa-l40s) | 4 → 4 | 59,424 → 53,835 | 9% | £1,397.25 | A → A |

Discovery: 24 servers, 920 cores and 9,472 GB of memory;
72,402 kWh a year, costing £18,100.50 in energy,
£28,800.00 in maintenance and £46,000.00 in licensing.
Servers by grade: A: 2, B: 16, C: 2, D: 4.

The EU compliance report, with the servers in Efficiency mode: PUE 1.4 (grade D), WUE 1.2 (grade G);
77,865 kWh a year in total (48,388 kWh by the servers);
6,339 kgCO2e scope 2 (the servers' own electricity) and 48,251 kgCO2e scope 3; 196 m³ of water.
All 24 servers rated. Each server's power matches the simulations': a Dell R640 at
30% utilisation averages about 145 W in both.

## What some figures cover

- **Money is exact.** The API gives each amount to the penny, as a string such as `"29231.50"`.
  Compare it as it is; don't round it first.
- **The yearly saving is more than the electricity.** `cost_savings.total` adds the saving on
  maintenance and licensing to the saving on electricity. `reductions_pct.operating_cost` is the
  electricity bill only, so in these reports it equals `reductions_pct.energy`.
- **Scope 2 on the compliance report is the servers' own electricity** times the grid's carbon
  intensity. It doesn't count the rest of the site (cooling, networking, storage), so it is
  smaller than the site's total energy times that intensity.
- **GPU Estate Refresh and GPU Host-to-Host give different energy for the same H100 and L40S
  servers, by design.** Both must do the work of the old GPUs at 60%.
  - Estate Refresh works out how many new servers that needs, rounds up to whole servers, and
    runs their GPUs at the same 60%.
  - Host-to-Host takes the number you chose and works out how busy their GPUs need to be. For
    the two H100 servers that's about 49%: 16 A100s at 312 TFLOPS and 60% busy, spread over
    8 H100s at 757 TFLOPS. Two servers are more than the work strictly needs, so their GPUs
    are less busy than 60%.

  GPUs use most of the power, so Host-to-Host's figure is lower: 29,480 against 32,582 kWh a
  year for the H100 servers.
- **In the GPU reports, `host_to_host.new_utilisation` is the CPUs' utilisation, not the
  GPUs'.** It's 53.85% for the H100 servers: the two new servers' CPUs do the work of the
  four old ones. The results don't yet include the GPUs' utilisation.
