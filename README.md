# Sati — Case Management for Social Services

Sati is a desktop case management application built for social services case managers. 
It helps track clients, document visits and contacts, monitor compliance deadlines, 
and calculate monthly productivity — all in one focused tool designed for daily use.

The name comes from the Pali word for mindfulness and remembrance, reflecting the 
app's purpose: keeping what matters present and accounted for.

---

## Features

**Client Management**
- Add and manage a caseload of clients with biographical info, waiver type, and effective dates
- Per-client compliance checklists tracking annual forms and quarterly reviews
- Automatic form deadline generation based on each client's effective date

**Notes & Documentation**
- Log visits, contacts, and documentation notes with date, status, and unit count
- Note templates auto-populate based on note type and form selection
- Status workflow: Scheduled → Pending → Logged, with automatic abandonment after a configurable threshold
- Full-text search and status filtering across all notes

**Productivity Tracking**
- Monthly unit totals broken down by status (Pending, Logged, Abandoned)
- Estimated incentive calculation based on logged units
- Workday scheduler for marking scheduled and excluded days

**Upcoming Events Dashboard**
- Automatically surfaces approaching form deadlines, scheduled visits, and scheduled contacts
- Sortable by date or event type

**Scratchpad**
- A persistent daily work log that auto-saves on close
- Full history browser with search across all previous entries

**Settings**
- Configurable abandonment threshold, productivity targets, incentive rates, and note templates
- Holiday and weekday exclusions for the workday scheduler

---

## Tech Stack

Sati is a WPF desktop application targeting .NET 10 on Windows.

- **UI Framework:** WPF with strict MVVM architecture
- **MVVM:** CommunityToolkit.Mvvm (ObservableObject, RelayCommand, ObservableValidator)
- **Data Access:** Entity Framework Core 10 with SQL Server LocalDB
- **Dependency Injection:** Microsoft.Extensions.DependencyInjection via IHost
- **Database:** SQL Server LocalDB (local), designed for Azure SQL migration

The architecture follows strict separation of concerns — ViewModels have no knowledge 
of Views, services are injected via constructor DI, and window creation uses the 
factory delegate pattern throughout.

---

## Screenshots

![Main window](images/Screenshots/mainwindow.png)
![Client List](images/Screenshots/client_list.png)


---

## Setup

Sati requires .NET 10 and Visual Studio 2022 or later on Windows. The project
targets `net10.0-windows` with WPF, so it builds and runs on Windows only.

1. Clone the repository
2. Copy `appsettings.template.json` to `appsettings.json` in the project root
   and fill in the `ConnectionStrings:SatiDb` value
3. Open `Sati.slnx` in Visual Studio (open the solution, not the folder) and set
   `Sati` as the startup project
4. Run. Pending EF Core migrations are applied automatically at startup
5. Create a user account from the login screen to get started

### Connection string

`appsettings.json` is gitignored so that database credentials never reach the
repository. Every machine needs its own copy — a fresh clone has no connection
string, and startup will fail at the migration step with
`The ConnectionString property has not been initialized` until one is supplied.

`appsettings.template.json` documents the required shape and both supported
Azure SQL authentication modes.

---

## Status

Sati is under active development. Core workflows are complete and in daily use.
The database runs on Azure SQL. Planned future work includes Microsoft Entra ID
authentication and MSIX packaging for deployment.

---

## About

Built by Josh — a social services case manager with a background in software 
development — as both a practical daily tool and a portfolio project demonstrating 
modern .NET desktop application architecture.
