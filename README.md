ColorSorterGUI

How to Run the Application
Requirements
- .NET SDK (version compatible with the project, e.g. .NET 7 or newer)
- Windows, macOS, or Linux
- Network access to the robot (if robot functionality is used)

Steps
- Clone or download the project repository.
- Open the project in an IDE that supports Avalonia (e.g. Visual Studio or JetBrains Rider).
- Restore NuGet packages if required.
- Build the solution.

Running the Application
Run the project using the IDE or with the following command:
- dotnet run

When the application starts, the database will be created automatically if it does not exist.
A default admin account is created on first startup:
- Username: admin
- Password: admin123

Using the Application
Log in using the default admin account or another created user.
Admin users can create additional users and open the ControlView.

In the ControlView:
- Use the color buttons to send sorting scripts to the robot.
- Use the Sort All function to run all sorting scripts in sequence.
- Monitor robot log messages in real time.
- Adjust inventory counts manually if needed.

Notes
If the robot is not available, the application can still be run to demonstrate login, database, and UI functionality.
Robot IP address and ports can be configured in RobotService.cs.
Computer IP adress needs to be manually set in URscripts. 
The database is stored locally in the user’s application data directory.




Overview
ColorSorterGUI is a desktop application developed in C# using Avalonia UI.
The application is used to control a robot-based color sorting system and integrates:
- Graphical user interface (GUI)
- Robot communication via TCP/IP
- Automatic inventory handling
- User authentication with roles (Admin / User)
- Local SQLite database

The system is designed as an educational project focusing on system design, control flow, and software architecture.

System Architecture
The application follows a layered architecture:
View layer
- LoginWindow
- AdminWindow
- ControlView
Service / Repository layer
- RobotService
- UserRepository
- InventoryRepository
- PasswordHasher
- Data / Model layer
- DatabaseService
- ComponentColor
Dependencies flow top-down:
Views depend on services and repositories, which in turn depend on shared data services and models.

Application Flow
Startup
- The application starts via Program.cs and App.axaml.cs.
- The database is initialized if it does not already exist.
- A default admin user is ensured (admin / admin123).
- The LoginWindow is displayed.
Login
- The user enters a username and password.
- Credentials are validated against the database.
- If authentication succeeds:
- Admin users are redirected to AdminWindow
- Regular users are redirected to ControlView
- If authentication fails, an error message is shown.
AdminWindow
The AdminWindow is only accessible to users with the Admin role.
Admins can:
- Create new users
- Assign roles (Admin or User)
- Open the ControlView
- Log out and return to the LoginWindow

ControlView – Robot and Inventory Control
Robot Communication
Robot communication is handled using TCP/IP:
- Robot scripts are sent to the robot on port 30002
- The GUI listens for robot log messages on a separate port
- Log messages are displayed live in the UI

Robot Log Processing
The system continuously processes incoming robot log lines using a loop.
Important log messages:
- STEP EyesLocate DONE cnt=n
  Stores the number of detected components (eyesCount)
- STEP Place DONE color=X
  Uses the previously stored count to update the inventory for the specified color
- Duplicate log messages are ignored to prevent double counting
This logic is implemented using if / else if / while control structures.

Inventory Management
Inventory data is stored in a local SQLite database
Each color (Red, Green, Blue) has an associated count
Inventory updates are performed using database transactions
Counts can be adjusted manually using GUI buttons

Sort All Function
The Sort All feature executes a predefined sequence of robot scripts:
- Blue
- Green
- Red

The next script is only sent when the robot reports:
- RUN <script_name> END
This ensures correct sequential execution.

Database Structure
Users table
- Username
- PasswordHash
- PasswordSalt
- Role
- CreatedAtUtc

Inventory table
- Color
- Count
- UpdatedAtUtc
The database is automatically created in the user’s application data directory.

Security
- Passwords are never stored in plain text
Passwords are hashed using:
- PBKDF2
- SHA-256
- Per-user salt
- Fixed-time comparison to prevent timing attacks

Technologies Used
- C#
- Avalonia UI
- SQLite
- TCP/IP networking
- URScript (robot control)


Notes
The default admin credentials are intended for demonstration and testing purposes only.
Hardware components such as camera and PLC are abstracted into the robot control logic.
The application is designed to be cross-platform.

Authors
This project was developed as part of an educational assignment focusing on:
- System interaction planning
- Control flow using if / else / while
- GUI-based robot control
- Database integration and security
