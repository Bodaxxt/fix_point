## FixPoint - Computer Maintenance Center System
**Windows Programming I (ICT-D-415) — Assignment 1 Technical Solution**  
*New Cairo Technological University (NCT) | Faculty of Industry & Energy Technology*  
*Software Engineering Track (SW) – Year 4*

---

### 📌 Project Overview
**FixPoint** is an enterprise-grade Windows Forms (.NET 8) desktop management system designed for computer maintenance centers. It orchestrates customer hardware repair workflows with strict data integrity, real-time GDI+ graphics visualization, multi-role security (RBAC), and high-DPI document printing.

---

### 🛡️ Multi-Role Security Accounts (RBAC)
| Role | Username | Password | Privileges & Accessible Interfaces |
| :--- | :--- | :--- | :--- |
| **System Admin** | `admin` | `Admin@123` | Full access to all modules, financial metrics, and record deletion. |
| **Front-Desk Reception** | `employee` | `Emp@123` | Customer & device intake, repair intake, invoices (Delete buttons hidden). |
| **Workshop Technician** | `tech` | `Tech@123` | Dedicated repair workshop view; diagnoses hardware and updates status. |

---

### 🚀 Key Technical Features
1. **Interactive GDI+ Lifecycle Progress Meter:** Custom owner-drawn pipeline on `Panel.Paint` rendering state progression (`Received` -> `In Progress` -> `Completed` -> `Delivered`).
2. **ACID SQLite Database Engine:** Connection-level `PRAGMA foreign_keys = ON;` and `PRAGMA journal_mode = WAL;`, strict 3NF schema, and B-Tree indexing.
3. **Optimized UI Performance:** Reflection-enabled `DoubleBuffered` controls (zero flicker) and debounced async database querying.
4. **Professional Invoice Printing:** Custom itemized A4 document rendering via GDI+ with payment tracking and balance breakdown.
5. **Universal Desktop Command Sync:** Unified `MenuStrip`, `ToolStrip`, and accelerator hotkeys (`Ctrl+S`, `Ctrl+P`, `Ctrl+N`).

---

### 📄 Assignment Solution Deliverable
The complete formal assignment solution report (Tasks 1 to 4, covering all 24 questions, Draw.io vector flowcharts, and live system screenshots) is included directly in this repository:
👉 **[FixPoint_Assignment_Solution.pdf](./FixPoint_Assignment_Solution.pdf)**

