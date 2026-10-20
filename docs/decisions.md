# Decisions

The main choices I made while building the app, why I made them, and what each one costs.
How the result works is described in [architecture.md](architecture.md).

1. [Dataset: NEA real-time air temperature](#1-dataset-nea-real-time-air-temperature)
2. [Keep a local copy and sync it on a timer](#2-keep-a-local-copy-and-sync-it-on-a-timer)
3. [Four-layer module structure](#3-four-layer-module-structure)
4. [Bulk insert in SQL rather than an entity loop](#4-bulk-insert-in-sql-rather-than-an-entity-loop)
5. [Let a unique index prevent duplicates](#5-let-a-unique-index-prevent-duplicates)
6. [Checkpoint on the newest stored reading and walk forward by day](#6-checkpoint-on-the-newest-stored-reading-and-walk-forward-by-day)
7. [Store UTC, format Singapore time on the server](#7-store-utc-format-singapore-time-on-the-server)
8. [Pull the sync decisions out into small functions](#8-pull-the-sync-decisions-out-into-small-functions)
9. [Leaflet and OpenStreetMap instead of Google Maps](#9-leaflet-and-openstreetmap-instead-of-google-maps)
10. [Login for everything, a role for the operational screen](#10-login-for-everything-a-role-for-the-operational-screen)
11. [Tests inside OutSystems and in this repo](#11-tests-inside-outsystems-and-in-this-repo)
12. [Infrastructure as a single-server proof of concept](#12-infrastructure-as-a-single-server-proof-of-concept)
13. [Shared styles in the theme](#13-shared-styles-in-the-theme)

---

## 1. Dataset: NEA real-time air temperature

The brief asked for a public data.gov.sg dataset with ways to explore it. The dataset
decides how much there is to actually build, so I wanted one that was alive rather than a
fixed table.

I chose NEA's real-time air temperature API (`/v2/real-time/api/air-temperature`) because:

- It is a proper API. I can query it over HTTPS by date and page through the results, which
  makes it a real integration rather than a one-off file import.
- The data is frequent and current. Around 18 stations report every minute, so the app always
  has fresh readings, and the data keeps growing (about 26,000 readings a day).
- It raises the problems an integration normally has to handle: pagination, rate limits,
  incremental sync, time zones, duplicates, and catching up after downtime.
- It gives the UI something worth exploring: stations on a map, a ranking of where it's
  hottest right now, and patterns over the day and the week.

Another data.gov.sg dataset was my first idea, but it was only available as a file download,
with no API to query. That would have meant a static import: no scheduling, no incremental
updates and data that goes out of date. I ruled out static datasets for that reason.

The downside is depending on someone else's live service and its rate limits, which shaped
decisions 2 and 6. The data is also measurements only: no forecasts, and no history from
before the app started collecting.

## 2. Keep a local copy and sync it on a timer

The screens could call the API directly, or read from a local copy.

I went with a local copy, synced every 5 minutes by a timer in the BL module. The screens only
read the database.

Calling the API from the screens would always be current, but every page view would spend
rate limit (6 requests per 10 seconds without a key). A 7- or 30-day chart would need hundreds
of calls, and the app would break whenever the API was slow or down.

The cost is that data can be up to about 5 minutes old, and I own the storage and the sync.
To keep that visible, the dashboard says how many minutes ago it was updated and switches to
"Stale" when the newest reading is more than 30 minutes old.

## 3. Four-layer module structure

I split the app along the OutSystems 4-layer canvas: `DataGovSG_IS` for the API,
`AirTemprature_CS` for the data, `AirTemprature_BL` for the sync, `AirTemprature` for the UI,
plus a separate test module.

Each module has one reason to change. If the API changes, only the IS module is affected; a
new screen only touches the UI. References only go one way, so there are no cycles, and
another app could reuse the data layer.

For an app this size it does mean more modules to publish in the right order, and more
"refresh dependencies" while developing.

## 4. Bulk insert in SQL rather than an entity loop

A normal 5-minute run only brings about 5 new timestamps, around 90 readings. The insert has
to handle the catch-up case as well, though: a full API page is 25 timestamps of 17 to 18
readings each (about 450 readings), and a whole day is around 26,000.

The usual OutSystems approach is to loop over the readings and call the entity's create
action for each one. It is easy to read in Service Studio, and fine for 90 rows, but catching
up would mean about 450 database calls per page and 26,000 per day, which would never fit in
the time budget.

Instead, each page is serialised to JSON and inserted with a single SQL statement: `OPENJSON`
unpacks it, a join finds each station by its external ID, and `INSERT … SELECT … WHERE NOT
EXISTS` adds only the new rows.

The trade-off is that advanced SQL is harder to follow than a visual flow, ties the module to
SQL Server (`OPENJSON` needs compatibility level 130 or higher), and needs tests to give
confidence, since you can't see the logic in the diagram. The BDD tests cover it.

## 5. Let a unique index prevent duplicates

`TempratureReading` has a unique index on station and reading time, and the insert uses `NOT EXISTS`.

This means the sync can fetch any page again without worrying: after a failure, a timeout,
overlapping days or a manual run. I don't have to track exactly what has already been
inserted, because the database enforces it.

Fetching a page twice still costs an API call and a little database work. The checkpoint stop
in decision 6 keeps that to a minimum.

## 6. Checkpoint on the newest stored reading and walk forward by day

Each run needs to know where to carry on from.

My first version asked the API for the checkpoint's own day. That worked until the day was
complete. After that, every run downloaded the same finished day again, inserted nothing, and
still reported success. I only noticed when the dashboard was showing three-day-old data while
Sync History was all green. (It also didn't help that the runs weren't recording the rows they
inserted. That is fixed too.)

How it works now:

- The checkpoint is `MAX(ReadingTime)` from the readings table, read once at the start of a run.
- The run goes through each Singapore day from the checkpoint's day up to today, but never
  more than 7 days back.
- Within a day, it stops paging as soon as a page reaches the checkpoint, because the readings come newest first.

I also considered keeping the checkpoint on the last successful `SyncRun`. I decided against
it because that value can drift away from what's actually stored (after a failed run or a
manual delete). `MAX(ReadingTime)` is always correct by definition, and the index makes it cheap.

Downsides: a gap older than 7 days isn't filled in automatically, which is an intentional
limit on API use. Each day processed also gets its own `SyncRun` record.

## 7. Store UTC, format Singapore time on the server

`ReadingTime` is stored in UTC. Grouping by Singapore hour or day happens in SQL, and any time
that appears as text on a screen is formatted on the server.

UTC is unambiguous to store and compare, and it matches the server clock. The server-side
formatting came out of a bug: a Reactive app converts every DateTime it sends to the browser
into the viewer's time zone, and my screens then added Singapore's 8 hours on top. Anyone
outside UTC saw the wrong times. I found it when the dashboard showed readings 5 hours in the
future from a browser set to UTC+5.

Because the labels arrive as ready-made text, the browser can't reformat them. For an app
about Singapore that's fine, and it means the format is decided in one place.

## 8. Pull the sync decisions out into small functions

The sync is mostly orchestration: API calls and commits. It can't be unit tested safely as it stands.

I moved its decisions into three small public functions that don't touch the database or the
API: `GetSyncOutcome` (is the run finished?), `IsTimeBudgetExceeded` (should it stop now?) and
`GetResumeStoredMax` (which date filter to use when resuming). `IsTimeBudgetExceeded` takes
the current time as a parameter instead of calling `CurrDateTime()`, so the tests can use fixed times.

It adds a little indirection to the flows. The orchestration itself (the page loop and the
commits) still isn't covered by automated tests. I check it through Sync History.

## 9. Leaflet and OpenStreetMap instead of Google Maps

The map uses the LeafletJS Forge component with OpenStreetMap tiles.

OutSystems Maps (Google Maps) looks more polished, but it needs a Google API key and a billing
account, plus managing that key per environment. Leaflet needs none of that, and open map data
suits an open-data app.

The component has its quirks. It writes shape names and marker icons straight into its
JavaScript, so names have to be valid JavaScript identifiers and custom icons broke it. I used
colour-coded circles with a card that opens on click instead of hover tooltips.

## 10. Login for everything, a role for the operational screen

Every screen needs a login, and Sync History also needs the `SyncMonitor` role.

The data is open, so a public dashboard would have been reasonable. But public screens expose
their server actions to anonymous traffic (OutSystems flags this), and for an app in a
government setting I would rather start from authenticated access.

It means anyone reviewing the app needs an account. On the other hand, operational details
like errors and row counts are only visible to people who need them, and the role is checked
on the screen itself, not just by hiding the link.

## 11. Tests inside OutSystems and in this repo

Inside OutSystems, BDDFramework tests cover the CS actions (each scenario is rolled back with
`AbortTransaction`) and the BL decision functions. In this repo, .NET tests with NUnit and
Playwright cover the data.gov.sg API contract and the main user journey in the deployed app.

Logic is easiest to test where it runs. The contract with the external API and the end-to-end
journey are best tested from outside, in CI. The API tests also run nightly, so if data.gov.sg
changes something, a test fails before the sync does.

That means two toolchains. The UI tests need a test account and a live environment, so they
run nightly and on demand rather than on every pull request.

## 12. Infrastructure as a single-server proof of concept

The Terraform creates one Windows server running IIS, OutSystems Platform Server and SQL Server
Express (from AWS's Windows plus SQL Express image), and only what that server needs: a VPC,
a public subnet, a security group, SSM access and an Elastic IP.

I first drafted a production-style setup with a load balancer, WAF, auto-scaling front-ends,
RDS Multi-AZ and a separate LifeTime environment. I dropped it: it was a lot to justify for
something that can't be deployed without an OutSystems licence, and it isn't where this
project's value is.

What's missing (high availability, a second availability zone, remote state, an automated
install) is listed in [infra/README.md](../infra/README.md), along with the order in which I
would add it.

## 13. Shared styles in the theme

The shared CSS (all classes start with `at-`) is in the theme, and I keep a copy in
[`outsystems/AirTemprature-theme.css`](../outsystems/AirTemprature-theme.css) so it's under
version control. Screens only keep rules that are specific to them. Values that depend on
the data, such as temperature colours and bar widths, are set inline, with the colours coming
from one server function, `GetTemperatureColor`.

The only friction is that the theme has to be pasted into Service Studio whenever the file
changes, because OutSystems doesn't read it from the repository.
