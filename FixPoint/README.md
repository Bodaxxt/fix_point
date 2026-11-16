# FixPoint

FixPoint is a Windows Forms desktop application for a computer maintenance center. It manages customers, devices, technicians, repair orders, invoices, and payments with a SQLite back-end.

## Default login

The application creates a default administrator account automatically on first run.

- Username: admin
- Password: Admin@123

The password is hashed before storage using SHA-256 and is never kept in plain text in the database.

## Database

The SQLite database is created in the user's Local AppData folder:

- %LOCALAPPDATA%\FixPoint\fixpoint.db

## Run the app

```bash
dotnet run --project FixPoint.csproj
```

## Features

- Login with admin/employee roles
- Dashboard with live statistics
- Customer management
- Device management
- Technician management
- Repair order lifecycle tracking
- Invoice generation for completed repairs
- Invoice preview and printing, including customer/device details, repair description and diagnosis, totals, balance due, and payment history
- Payment recording with remaining-balance validation
