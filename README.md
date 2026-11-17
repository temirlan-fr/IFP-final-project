QueueLess

Campus queue and appointment management

QueueLess is a C Sharp and Blazor application that helps students arrange visits to a campus service centre. Students can join a live queue, follow their ticket and book an arrival window. Operators call visitors in arrival order, complete visits and pause services when necessary. A public display shows ticket numbers without revealing personal details.

Team

Temirlan Bolat
Aikyn Sabit

Project purpose

The aim is to reduce the need to stand physically in line while keeping queue order understandable and fair. The application brings student services, IT support, finance and library support into one workspace. It demonstrates reusable Blazor components, functional domain rules, database transactions, role based authorization and automated testing.

Requirements

Install the .NET 10 SDK. A modern browser and an internet connection are needed for the initial package restore. After dependencies are installed, the application works locally without paid services, API keys or a separate database server. Local verification was performed on Windows. GitHub Actions build and C Sharp tests also passed on Windows, Linux and macOS. Browser and real-device checks are recorded separately.

Quick start

Open the repository root in VS Code and run the following commands in its terminal.

dotnet restore QueueLess.sln
dotnet run --project src/QueueLess.Web --launch-profile http

Open http://localhost:5180 in your browser. Keep the terminal running while using the application. Press Ctrl and C in the terminal to stop the server.

The first launch creates a SQLite database, four services and two demonstration accounts. Development mode is selected by the http launch profile. No demonstration tickets or invented statistics are inserted. Queue counts change when real actions are performed.

Demonstration accounts

Student email is student@queueless.demo
Operator email is operator@queueless.demo
The password for both demonstration accounts is QueueLess2026!

These are public local demonstration credentials, not production credentials. The accounts are only seeded when the environment is Development. New accounts can be registered through the interface and always receive the Visitor role.

Main features

The overview shows service availability, expected waiting times, queue sizes and a personal ticket summary. Visitors can search service descriptions or filter by category. Paused services cannot accept new arrivals.

My tickets shows the active ticket and the latest twenty visits. A visitor can have only one active ticket across the whole centre. Waiting tickets can be cancelled after confirmation. Serving tickets must be finished by an operator.

Appointments provides half-hour arrival slots between 09:00 and 17:30, up to seven days ahead. All appointment times use campus time at UTC plus five hours. Each service has one reservation per slot. A visitor cannot reserve two services at the same time and can hold up to three upcoming reservations.

Check-in opens ten minutes before the appointment and closes fifteen minutes afterward. Checking in creates a normal live queue ticket in arrival order. An appointment reserves an arrival window, not a guaranteed service start or priority over people already waiting. Cancellation releases the reservation for another visitor. To reschedule, cancel the old reservation and book a new slot.

The operator desk provides service selection, pause and reopen controls, next visitor calling, completion and no-show outcomes. Only one visitor can be served by a desk at a time. Operators can also review daily outcomes and the latest centre activity.

The live display is public. It shows the currently called ticket and counter for each service, queue counts and estimated waits. It never shows account names, emails or password information.

Live updates and waiting times

Blazor Interactive Server uses a SignalR connection to update connected interfaces. Queue changes are published through a shared notification service after the transaction commits. Page subscriptions are removed when the page is disposed. One server heartbeat refreshes time dependent states every thirty seconds and marks missed appointments and previous-day tickets.

Waiting time is the number of waiting visitors ahead multiplied by the configured service duration, plus one service duration if a visitor is currently being served. It is an estimate, not a precise countdown. The current implementation uses one counter per service and configured service durations rather than a learned prediction model.

Architecture

QueueLess.Core contains entities, immutable result records and pure domain rules. These rules have no database, clock or user interface dependency.

QueueLess.Infrastructure contains the Entity Framework context, transactional queue operations, booking rules, operator operations and the live notification service. A fresh context is created for every operation. A process wide write gate serializes writes in this single-instance application. SQLite unique indexes protect against duplicate active tickets, duplicate numbers, simultaneous serving tickets and double booked slots.

QueueLess.Web contains the Blazor pages, reusable components, application composition, account endpoints and background maintenance. Services are injected through dependency injection. Authentication state is cascaded to components. Service cards use parameters and event callbacks. Error boundaries and operation results provide recoverable feedback.

QueueLess.Tests contains pure unit tests, integration tests against real temporary SQLite databases and HTTP flow tests using the complete ASP.NET application. The browser directory contains Playwright workflows for desktop Chromium, Firefox and mobile WebKit.

Data and security

The database is stored in src/QueueLess.Web/App_Data/queueless.db. Authentication keys are stored under App_Data/keys. Runtime data, keys, test output and build files are ignored by Git. Restarting the application preserves visits and accounts.

Passwords are hashed using the ASP.NET Core password hasher. Account forms use antiforgery tokens. Authentication uses an HTTP-only session cookie with a fixed eight-hour lifetime. Queue service operations check session expiry and current database roles. Visitors cannot modify another account's ticket or reservation. Account endpoints are rate limited to fifteen requests per minute per IP address. User input is validated on the server and rendered with normal Razor escaping.

The application is a coursework implementation for a single centre and a single server process. Before public deployment, replace demonstration access, provision operators securely, configure HTTPS and protect the data directory with operating system permissions. Do not publish an existing development database. Email verification, password recovery, multi-instance event distribution, configurable campus timezones and production schema migrations are outside this version's scope.

Testing

Run the automated C Sharp tests with the following command.

dotnet test QueueLess.sln

To collect coverage, use the following command.

dotnet test QueueLess.sln --collect:"XPlat Code Coverage"

The application baseline passed 62 tests with no failures. Eight additional production-hosting cases cover bootstrap validation, secure cookies and persistence after restarting the host. The baseline measured line coverage was 93.57 percent overall, 96.15 percent in Core and 99.44 percent in Infrastructure. These measurements concern instrumented C Sharp code and do not prove visual correctness or browser coverage. See docs/TESTING.txt for the exact verification scope.

To run browser tests, install Node.js, enter tests/browser, run npm install, then run npx playwright install and npm test. The configuration starts the application automatically if it is not already running. Browser tests create demonstration accounts and tickets, so use a development database. All twelve browser scenarios passed in GitHub Actions across Chromium, Firefox and mobile WebKit. The workflow uploads its report and screenshots.

Project documentation

docs/USER_GUIDE.txt explains student and operator workflows.
docs/DEVELOPER_GUIDE.txt explains architecture, persistence, security, API endpoints and deployment boundaries.

docs/DEPLOYMENT.txt explains how to deploy the Docker container from GitHub to Railway with a persistent volume and a private operator account. Deployment requires a connected hosting account. GitHub Pages does not support this server application.
docs/CRITERIA.txt maps all twenty assessment criteria to implementation evidence and limitations.
docs/TESTING.txt records automated results and the manual verification checklist.
docs/TEAM_AND_DEFENSE.txt assigns presentation responsibilities to Temirlan Bolat and Aikyn Sabit and explains the main technical decisions.
docs/PRESENTATION_OUTLINE.txt contains English slide content and a timed defence plan.
docs/architecture.mmd and docs/data-model.mmd contain editable Mermaid diagrams.

Team responsibilities

Temirlan Bolat is assigned to review, extend and present domain logic, SQLite persistence, authentication, operator actions and integration testing. Aikyn Sabit is assigned to review, extend and present the Blazor interface, reusable components, ticket views, appointment workflow, responsive design and browser testing.

This is a responsibility and learning allocation. It is not a statement that either student personally authored the generated implementation. Both students must make and understand their own programming contributions and be ready to explain any part of the project. Record actual contributions in the team document before submission.

Development history

The repository was developed through separate commits and pushes for the solution structure, queue persistence, authentication, visitor workspace, appointments, operator workflows, interface recovery and testing. Documentation and corrections are delivered in further commits. The history records actual development stages without invented dates or authorship.

References

Microsoft Blazor authentication documentation
https://learn.microsoft.com/aspnet/core/blazor/security/?view=aspnetcore-10.0

Microsoft Entity Framework SQLite documentation
https://learn.microsoft.com/ef/core/providers/sqlite/

Microsoft Entity Framework concurrency documentation
https://learn.microsoft.com/ef/core/saving/concurrency
