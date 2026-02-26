Version: 2.1
Last Updated: 26/02/2026
Updated By: Auto-Documentation System

# Smart E-Library

## 1. Project Title
Smart E-Library

## 2. Project Overview (simple explanation)
Smart E-Library is a web application for managing study materials (chapters/notes), quizzes, and student learning progress.

It helps:
- Admins manage semesters, subjects, teachers, and students
- Teachers upload learning content and quizzes
- Students read content, take quizzes, and track their progress

## 3. Project Definition
Smart E-Library is an online learning library and progress-tracking system.

It stores educational content by semester/subject/topic and records:
- Which pages a student visited
- How much time they spent reading
- Quiz scores
- Overall learning progress analytics

## 4. Project Objectives
- Provide a central place for learning materials
- Support structured learning by semester → subject → topic
- Allow teachers to create chapters with multiple pages and optional quizzes
- Track student progress in a measurable way (time, completion, quiz score)
- Provide analytics dashboards for teachers and administrators

## 5. Project Scope
**In scope (currently implemented):**
- User management with three roles: Admin, Teacher, Student (role details stored in separate role tables)
- Login flows:
  - Admin login with password
  - Teacher login with password
  - Student login using OTP (one-time password)
- Admin:
  - Manage users (approve/deny)
  - Create semesters and subjects
  - Assign teachers to subjects
  - Review and decide semester enrollment requests
  - View system-wide progress reports
- Teacher:
  - Upload chapter-style materials as rich-text pages
  - Optionally attach a quiz after a page (multiple questions supported)
  - Create quizzes
  - View student progress analytics for the teacher’s own materials
- Student:
  - Enroll in a semester (requires admin approval)
  - Browse the library and read materials after approval
  - Read chapter pages in order (with completion tracking)
  - Attempt quizzes after approval

**Out of scope (not implemented / not guaranteed):**
- Payment features
- Live classes/video streaming
- Real SMS gateway integration (OTP SMS is a placeholder)
- Advanced admin reporting filters (beyond the current dashboard)

## 6. Technologies Used
### Frontend
- ASP.NET Core MVC Razor Views (server-rendered UI)
- HTML + Bootstrap-style UI (based on existing views)
- JavaScript (for basic interactivity where needed)

### Backend
- .NET 10 (ASP.NET Core)
- ASP.NET Core MVC Controllers
- Session-based authentication/authorization (custom role filter)
- Service layer for OTP and progress calculations

### Database
- MySQL (via `Pomelo.EntityFrameworkCore.MySql`)
- Entity Framework Core (EF Core) for ORM and migrations

### Tools & Platforms
- .NET SDK (project targets `net10.0`)
- Entity Framework Core Migrations
- Visual Studio Code / Visual Studio (typical development tools)

## 7. System Architecture Overview (explained in text form)
The system uses a simple “web app + database” architecture:

1) The user opens the web application in a browser.
2) The browser sends requests to ASP.NET Core Controllers (backend).
3) Controllers:
   - Check the user session (who is logged in and which role)
   - Load or save data using EF Core
   - Return a Razor View (HTML page) to the browser
4) The database (MySQL) stores all permanent records like users, semesters, materials, quiz questions, quiz results, and progress.

Key internal parts:
- **Controllers**: handle pages like Admin Dashboard, Teacher Upload Material, Student Library, etc.
- **Filters**: `RoleAuthorizeAttribute` blocks pages for users who are not allowed.
- **Services**:
  - OTP generation/verification
  - Progress score calculation
- **EF Core DbContext** (`ApplicationDbContext`): defines all tables (entities).

## 8. Database Details
### Database Name
Default database name (configurable): `smartelibrary_db`

Where it is defined:
- In configuration under `Database:Name` (and related host/port/user/password)
- Or via `ConnectionStrings:DefaultConnection` if provided

### List of Tables
Based on the current EF Core model, the database tables are:
- Admins
- Teachers
- Students
- Users
- Semesters
- Subjects
- Topics
- TeacherSubjects
- StudentEnrollments
- Materials
- MaterialPages
- MaterialPageProgress
- Quizzes
- QuizQuestions
- QuizResults
- ProgressTrackings
- OtpVerifications

### Table Description (simple)
- **Users**: Stores shared identity and login fields used for authentication and authorization (phone, password hash, role, approval status, created date, etc.).
- **Admins/Teachers/Students**: Store role-specific fields and link back to Users (one-to-one).
- **Admins**: Stores admin-specific details and links to Users (one-to-one).
- **Teachers**: Stores teacher-specific details and links to Users (one-to-one). Includes **Teacher ID** (column: `TeacherId`, format: `TID0001`).
- **Students**: Stores student-specific details and links to Users (one-to-one). Includes Enrollment Number.
- **Semesters**: Stores semester list (e.g., “Semester 1”), and whether it is active.
- **Subjects**: Subjects belong to a semester (e.g., “Math” in “Semester 1”).
- **Topics**: Topics belong to a subject (optional grouping such as “Algebra”).
- **TeacherSubjects**: Mapping table that assigns teachers to subjects (who can teach/upload for what).
- **StudentEnrollments**: Records which student is enrolled in which semester, including admin approval fields.
- **Materials**: Learning materials uploaded/created by teachers. Each material belongs to a semester and subject (and optional topic). Some materials can be public.
- **MaterialPages**: A material can have multiple pages (rich-text/HTML content) with page numbers.
- **MaterialPageProgress**: Tracks page-level reading progress for each student (time spent, started/completed times, completed flag).
- **Quizzes**: Quizzes created by teachers. A quiz belongs to a subject and teacher; it may also be linked to a material and/or a specific material page.
- **QuizQuestions**: Stores questions and options for a quiz.
- **QuizResults**: Stores a student’s quiz attempt score and submission time.
- **ProgressTrackings**: Stores overall progress values (screen time, completion, quiz score, progress percent, low engagement flag) for a student per subject/topic/material.
- **OtpVerifications**: Stores OTP codes and validity status for phone verification.

### Relationships
In simple terms:
- One **Semester** has many **Subjects**.
- One **Subject** has many **Topics**.
- One **Teacher (User)** can upload many **Materials**.
- One **Material** has many **MaterialPages**.
- One **MaterialPage** can have many **MaterialPageProgress** records (one per student).
- One **Quiz** has many **QuizQuestions**.
- One **Quiz** has many **QuizResults** (one per student attempt).
- **TeacherSubjects** connects Teachers ↔ Subjects (many-to-many).
- **StudentEnrollments** connects Students ↔ Semesters (many-to-many) and includes admin approval.
- **Admins**, **Teachers**, and **Students** each link one-to-one to **Users**.

Approval and tracking highlights:
- **StudentEnrollments** includes: `IsApproved`, `ApprovedAt`, `ApprovedByAdminId`.
- **MaterialPageProgress** stores reading time and completion.

### ER explanation in simple language
Think of the database like linked lists of information:
- First you create semesters.
- Inside each semester you add subjects.
- Teachers are assigned to subjects.
- Teachers create materials and pages for those subjects.
- Students enroll in semesters.
- Students read pages and take quizzes.
- The system stores progress for each student so teachers/admins can see analytics.

## 9. Functional Requirements
### Authentication & Access
- Users register via separate Teacher and Student registration pages.
- Phone verification is done using OTP.
- Teachers and students must be approved by Admin before they can access their dashboards.
- Each panel is role-protected:
  - Admin pages for Admin only
  - Teacher pages for Teacher only
  - Student pages for Student only

### Admin Features
- View dashboard summary (total users, students, teachers, materials, quizzes).
- Approve users.
- Edit or delete users (with rules to prevent deleting system admin or users with dependencies).
- View detailed user list with teacher IDs and student enrollment details.
- Create and manage semesters.
- Create and manage subjects.
- Assign teachers to subjects.
- Approve or reject semester enrollment requests.
- View students grouped by semester.
- View system progress analytics report.

### Teacher Features
- View dashboard summary (material count, quiz count, low engagement count).
- Upload chapter materials with:
  - Title and description
  - Multiple pages (page title + rich text content)
  - Page length guidance (encourages splitting long content into separate pages)
  - Optional quiz after a page (multiple questions supported; quiz title optional)
- Insert or delete pages before saving, with automatic page re-sequencing
- View uploaded materials.
- Preview chapter pages.
- Create quizzes.
- View “Student Progress Analytics” for students who interacted with the teacher’s materials.
- Each teacher is assigned a unique Teacher ID (e.g., `TID0001`).

### Student Features
- Login using OTP.
- Enroll in semesters and wait for approval.
- View dashboard (recent progress items and averages).
- Browse library materials by semester/subject and search.
- Read materials:
  - Chapters can be read page-by-page
  - Pages must be completed in order
  - A minimum reading time per page is required before completion (3 minutes)
- Quizzes:
  - Quizzes can be linked to pages
  - Page quizzes can include multiple questions and appear in a modal popup
  - Passing condition is used (example: 50% or above)

### Progress Analytics Formula (Current)
Student final progress is calculated using weighted metrics:
- Screen Time % → 50%
- Quiz Score % → 40%
- Completion % → 10%

Formula:
- Final Progress (%) = (Screen Time % × 0.50) + (Quiz Score % × 0.40) + (Completion % × 0.10)

Status bands used in analytics:
- Skimmer: < 20%
- NeedsImprovement: ≥ 20% and < 40%
- Learning: ≥ 40% and < 60%
- Progressing: ≥ 60% and < 80%
- ActiveLearner: ≥ 80% and < 90%
- Mastered: ≥ 90%

## 10. Non-Functional Requirements
### Performance
- Pages should load quickly for normal classroom-sized usage.
- Progress analytics should return results without long delays.
- Database queries should be optimized for common dashboards.

### Security
- Passwords are stored as hashes (not plain text).
- Role-based access control is enforced using a server-side filter.
- Sessions are used to store login state.
- OTP expires (time-limited) and can only be used once.

### Usability
- UI should be simple and readable for teachers and students.
- Workflows should require minimal steps (login → dashboard → action).
- Clear messages should be shown for invalid login/OTP.

### Scalability
- The system should be able to scale by:
  - Running multiple web servers (with a shared session strategy if needed)
  - Using a stronger database server
  - Adding caching for heavy reports

### Reliability
- Data consistency is enforced with database relationships and unique indexes.
- Critical operations (OTP verification, quiz submissions, page completion) must store data reliably.

## 11. User Roles & Permissions
### Admin
Can:
- Approve and manage users
- Create semesters and subjects
- Assign teachers to subjects
- View system-wide reports

### Teacher
Can:
- Manage their own materials and quizzes
- View progress analytics for students who used their materials

Cannot:
- Access admin management functions

### Student
Can:
- Enroll in semesters
- Access library materials (based on enrollment and public materials)
- Read pages and attempt quizzes

Cannot:
- Upload materials
- Manage other users

## 12. Application Workflow (Step-by-step process)
### A) Teacher Registration
1. Teacher opens the Teacher Registration page.
2. Teacher enters name, phone number, and password.
3. System creates the account and Teacher record (with Teacher ID like `TID0001`).
4. System generates OTP.
5. Teacher verifies OTP.
6. Teacher waits for Admin approval.

### B) Student Registration
1. Student opens the Student Registration page.
2. Student enters name, enrollment number, phone number, and password.
3. System creates the account and Student record.
4. System generates OTP.
5. Student verifies OTP.
6. Student waits for Admin approval.

### C) Admin Workflow
1. Admin logs in.
2. Admin opens Users page.
3. Admin approves teachers/students.
4. Admin creates semesters and subjects.
5. Admin assigns teachers to subjects.
6. Admin reviews semester enrollment requests and approves/rejects.
7. Admin can view students grouped semester-wise.
8. Admin views system reports.

### D) Teacher Workflow
1. Teacher logs in.
2. Teacher opens Upload Material.
3. Teacher selects assigned subject (semester auto-fills).
4. Teacher adds one or more pages.
5. Teacher can add a quiz after a page (multiple questions).
6. Teacher saves material.
7. Teacher views student progress analytics.

### E) Student Workflow
1. Student logs in with phone number → OTP.
2. Student enrolls in a semester.
3. Student waits for admin approval.
4. Student opens Library after approval.
5. Student selects a material and reads it.
6. Student reads each page for the required minimum time (3 minutes) before completing it.
7. System records page progress (started/completed/time).
8. If a quiz is attached to the page, it appears as a popup (modal) and the student answers it.
9. Student views their dashboard and progress updates.

## 13. Deployment Details
### Local Development
- Default HTTP URL: `http://localhost:5079`
- Launch profiles are defined in `Properties/launchSettings.json`.

Steps:
1. Configure MySQL connection details in appsettings (host/port/name/user/password), or set `ConnectionStrings:DefaultConnection`.
2. Ensure MySQL server is running and accessible.
3. Run the application using `dotnet run`.

### Database Setup
- The application applies schema changes using EF Core migrations (`Database.Migrate()`), not `EnsureCreated`.
- Migrations keep data safe and allow controlled updates.
- On startup the system can also run a temporary backfill to ensure role tables (Admins/Teachers/Students) match existing Users.

### Production Notes (high level)
- Store database credentials securely (not in source code).
- Use HTTPS and a reverse proxy (IIS/Nginx/Apache) if needed.
- Consider a distributed session approach if running multiple servers.

## 14. Limitations
- OTP SMS sending is a placeholder (no real SMS gateway integration).
- Session-based authentication may need redesign for large-scale deployments.
- Reporting is currently focused on progress analytics and may not cover all business reports.
- Material content storage uses rich-text HTML for chapter pages; file uploads depend on implementation details and server storage.

## 15. Future Enhancements
- Integrate a real SMS provider for OTP delivery.
- Add email notifications for approval and quiz completion.
- Add advanced filters for reports (by semester/subject/topic/date range).
- Add downloadable report export (PDF/Excel).
- Add better audit logs (who changed what and when).
- Improve scalability with caching and background jobs for heavy analytics.

## 16. Version History
| Version | Date | Changes |
|---------|------|---------|
| 2.1 | 26/02/2026 | Updated student progress analytics formula to 50/40/10 (Screen Time/Quiz/Completion) and documented active status bands (Skimmer → Mastered) |
| 2.0 | 22/02/2026 | Major architecture and workflow improvements (normalized role tables, separate registration, admin approvals, semester enrollment approvals, page-based content, enhanced quizzes, engagement tracking, Teacher ID rename) |
| 1.0 | 21/02/2026 | Initial documentation created based on current project state |
| 1.1 | 21/02/2026 | Added Admin/Teacher/Student role tables, Teacher Code, and enrollment approval fields |
| 1.2 | 21/02/2026 | Split registration into Teacher and Student flows and added dedicated pages |
| 1.3 | 21/02/2026 | Added semester enrollment approval and restricted library access until approval |
| 1.4 | 21/02/2026 | Enhanced admin users view and added semester-wise student grouping page |
| 1.5 | 21/02/2026 | Enforced page-based content rules and soft length validation for teacher uploads |
| 1.6 | 21/02/2026 | Added page insertion/deletion controls and edit support for rich-text chapters |
| 1.7 | 21/02/2026 | Added page-level multi-question quizzes with modal student experience |
| 1.8 | 21/02/2026 | Enforced minimum reading time before completing a page |

## 17. Conclusion
Smart E-Library provides a clear structure for learning content, quizzes, and progress tracking.

It supports three main user roles (Admin, Teacher, Student) and enables admins and teachers to monitor student engagement and learning outcomes using stored progress data.
