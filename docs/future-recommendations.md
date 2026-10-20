# Future recommendations

What I would do next, given more time. The first two items are things I started or planned
but left out because of the time available; the rest come from what I learned building and
testing the app.

| # | Recommendation | Effort | Value |
|---|---|---|---|
| 1 | Manual sync run from the UI | Small | High for operations |
| 2 | Production-ready Terraform with modules | Medium | Needed before any real deployment |
| 3 | More weather datasets from data.gov.sg | Medium | Makes the app far more useful |
| 4 | Sync hardening (errors, retries, settings) | Small | Fewer silent failures |
| 5 | Data model clean-up | Small to medium | Simpler SQL, better performance |
| 6 | Data growth and performance | Medium | Keeps the app fast after months of data |
| 7 | Monitoring and alerting | Small | Problems found before users notice |
| 8 | Testing and delivery pipeline | Medium | Confidence in every release |
| 9 | UI improvements | Small to medium | Better experience on the map and on mobile |
| 10 | Security | Small | Production-grade access control |

---

## 1. Manual sync run from the UI

Most of this is already built. I left the last part out because of time.

**What exists**
- The `SyncType` static entity has a `Manual` value, and `SyncRun` has `FromDate`, `ToDate`
  and `PaginationToken` fields.
- A second timer, `AirTempratureSync`, picks up a `Manual` run that is `Running` and resumes
  it from its stored pagination token. The decision of which date filter to send is in
  `GetResumeStoredMax`, which already has BDD tests.
- `WakeManualSyncTimer` in BL is public, so the UI can start the timer.

**What's missing**
- A "Sync now" or "Back-fill a date range" panel on Sync History, for users with the
  `SyncMonitor` role. It would create a `SyncRun` with `SyncTypeId = Manual`, the chosen
  `FromDate` / `ToDate` and status `Running`, then call `WakeManualSyncTimer`.
- A guard against starting a manual run while another run is in progress, since both timers
  write to the same tables (the inserts are idempotent, but two runs would waste API calls).
- The manual flow should stop at `ToDate`, and record `TriggeredByUserId` so Sync History shows who started it.
- BDD scenarios for creating the run and for the overlap guard.

This would also give a supported way to fill gaps older than the 7-day catch-up limit.

## 2. Production-ready Terraform with modules

The current Terraform is one server on purpose. For a real environment I would split it into
reusable modules and one small configuration per environment:

```
infra/
├── modules/
│   ├── network/        VPC across 2 AZs, public / private / data subnets, NAT, flow logs
│   ├── edge/           Application Load Balancer, ACM certificate, WAF, access logs
│   ├── frontend/       Windows front-end servers in an Auto Scaling group
│   ├── controller/     Deployment Controller (single instance with auto-recovery)
│   ├── database/       RDS SQL Server Standard, Multi-AZ, encrypted, password in Secrets Manager
│   ├── messaging/      Amazon MQ (RabbitMQ) for OutSystems cache invalidation
│   ├── observability/  CloudWatch alarms and an SNS topic
│   └── backup/         AWS Backup plan for RDS and EBS
└── envs/
    ├── dev/  test/  prod/  lifetime/
```

Alongside the modules:

- **Remote state** in an S3 bucket with locking, one state file per environment.
- **`terraform plan` in CI** through GitHub OIDC and a read-only AWS role, so no access keys are stored anywhere.
- **Automated installation**: a pre-built AMI with Platform Server installed (EC2 Image
  Builder) or SSM documents, instead of installing by hand.
- **Separate AWS accounts** per environment under AWS Organizations, with CloudTrail,
  GuardDuty and Security Hub enabled.
- A **LifeTime** environment on its own infrastructure, as OutSystems requires, to manage
  deployments between dev, test and prod.

## 3. More weather datasets from data.gov.sg

data.gov.sg publishes several other real-time weather APIs from NEA. Rainfall, relative
humidity, wind speed and wind direction use the same structure as air temperature
(stations plus timestamped readings), so most of the work is already done.

Suggested approach:

- **Generalise the readings.** Add a `MeasureType` static entity (temperature, rainfall,
  humidity, wind speed, wind direction) and store all readings in one table keyed by station,
  measure and time, or one table per measure if the volumes differ a lot.
- **Reuse the integration.** The IS module and the bulk insert only need the endpoint and the
  measure as parameters. The sync loops over the measures; each one adds about one API call
  per run, which still fits easily within the rate limit (and more so with an API key).
- **Add forecasts and air quality** later: the 2-hour and 24-hour forecasts, PM2.5 and PSI
  readings, and the UV index. These have different shapes (areas or regions rather than
  stations), so they need their own structures and screens.

On the UI side, this opens up a lot:

- a "current conditions" card per station (temperature, humidity, rain, wind);
- a "feels like" heat index computed from temperature and humidity;
- a rainfall layer on the map, and wind arrows;
- the 2-hour forecast next to the live readings.

## 4. Sync hardening

- **Exception handling.** Add an exception handler to the sync flow that rolls back the
  current page, marks the run `Failed` with the error message, and commits. Mark runs that
  stop at the time budget as `PartiallySucceeded` rather than leaving them `Running`.
- **Clean up stuck runs.** At the start of each run, close any `Running` run older than the
  timer timeout.
- **Retry on rate limiting.** Handle HTTP 429 with a wait and a few retries, with backoff.
- **Settings instead of constants.** Move the page wait (3.5 s), the time budget (15 min) and
  the catch-up window (7 days) to Site Properties, so they can be tuned per environment without publishing.
- **Station updates.** Update a station's name and coordinates when NEA changes them, instead
  of only inserting new stations.

## 5. Data model clean-up

- Store `Temprature`, `Latitude` and `Longitude` as Decimal instead of Text. Today the SQL has
  to use `TRY_CAST`, and the values can't be sorted or averaged directly.
- Fix the "Temprature" spelling in entity and module names. It needs a new entity and a
  one-off data migration, so it's best done together with the type change.
- Record `RowsFetched` and `RowsSkipped` on each run as well as `RowsInserted`.

## 6. Data growth and performance

The readings table grows by about 26,000 rows a day, around 9.5 million a year.

- **Hourly and daily summary tables**, filled by a small timer, so the 7-day and 30-day views
  and the island profile read a few hundred rows instead of tens of thousands.
- **A retention policy**: keep minute-level readings for a period (for example 90 days) and
  the summaries for longer. Deletes should run in batches from a timer, not in one statement.
- **Cache** the dashboard data action for a minute or two, since every user sees the same data.

## 7. Monitoring and alerting

- **Alert when data goes stale**: if the newest reading is more than 30 minutes old, email the
  `SyncMonitor` users. Today the dashboard only shows a "Stale" banner, which someone has to see.
- Show the failure count on Sync History and link each failed run to its error log.
- Use Service Center logs and OutSystems monitoring (or AppDynamics / CloudWatch on the AWS
  setup) to track timer duration and API response times.

## 8. Testing and delivery pipeline

- **An end-to-end test of the sync** against a stubbed API: the page loop, the checkpoint
  stop, a page failing halfway, and the time budget.
- **Run the BDD tests from CI** using the BDDFramework / OutSystems test automation APIs, and
  fail the pipeline if any scenario fails.
- **Broader UI tests**: Sync History with and without the role, search on the ranking, the
  map's station card, and accessibility checks with axe-core through Playwright.
- **Deployment through LifeTime**: dev, test and prod environments, with deployments promoted
  between them and the BDD and UI tests run after each one.
- A **dedicated test user** for the CI UI tests instead of a personal account.

## 9. UI improvements

- **Map tooltips.** The LeafletJS component writes marker names and icons straight into its
  JavaScript, which is why I used click-to-open cards. A small custom JavaScript integration,
  or a different map component, would allow hover tooltips and labelled markers.
- **Mobile layout.** The screens are responsive, but the map and ranking side by side are
  cramped on a phone; a tabbed layout would work better.
- **Comparing stations** on one chart, and exporting a station's data to CSV.
- **Auto-refresh** of the dashboard every minute or two while the page is open.

## 10. Security

- Single sign-on (for example Singpass or the organisation's identity provider) and MFA
  instead of local OutSystems users.
- Least-privilege roles beyond `SyncMonitor`, for example a separate role for starting manual syncs.
- A data.gov.sg production API key stored as a Site Property per environment, and rotated regularly.
