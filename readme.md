# Academia Auditiva 🎵

**Academia Auditiva** is a comprehensive web-based ear training platform designed to help musicians and music students develop their auditory perception skills through interactive exercises and gamification.

![.NET Core](https://img.shields.io/badge/.NET%20Core-8.0-blue)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-8.0-purple)
![Entity Framework](https://img.shields.io/badge/Entity%20Framework-Core-green)
![License](https://img.shields.io/badge/license-MIT-blue)

## 🎯 Features

### 🎼 Ear Training Exercises
- **Note Recognition** - Identify individual musical notes
- **Interval Training** - Recognize simple and complex intervals
- **Chord Recognition** - Identify chord types and qualities
- **Harmonic Function** - Understand tonal relationships
- **Melodic Dictation** - Transcribe melodies and identify missing notes
- **Quality Recognition** - Distinguish between major, minor, diminished chords

### 🏆 Gamification System
- **Achievement Badges** - Unlock badges for consistent practice and skill mastery
- **Progress Tracking** - Monitor your improvement over time
- **Difficulty Levels** - Beginner, Intermediate, and Advanced exercises
- **Performance Analytics** - Detailed insights into your learning journey

### 📊 Advanced Analytics
- **User Dashboard** - Comprehensive overview of your progress
- **Performance Metrics** - Track accuracy, response time, and improvement
- **Personalized Recommendations** - Get exercise suggestions based on your performance
- **Historical Data** - View your learning timeline and achievement history

### 🌐 Multi-language Support
- English (en-US)
- Portuguese (pt-BR)
- French Canadian (fr-CA)

## 🚀 Technology Stack

- **Backend**: ASP.NET Core 8.0 (MVC)
- **Database**: SQL Server with Entity Framework Core
- **Authentication**: ASP.NET Core Identity + External providers (Facebook)
- **Frontend**: HTML5, CSS3, JavaScript, Bootstrap
- **Audio**: Tone.js for web audio synthesis
- **Cloud**: Azure Key Vault, Azure Storage
- **Localization**: Built-in ASP.NET Core localization

## 📋 Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server) (LocalDB, Express, or full version)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) or [Visual Studio Code](https://code.visualstudio.com/)
- Modern web browser with Web Audio API support

## 🛠️ Installation & Setup

### 1. Clone the Repository
```bash
git clone https://github.com/lcarli/AcademiaAuditiva.git
cd AcademiaAuditiva
```

### 2. Restore Dependencies
```bash
cd AcademiaAuditiva
dotnet restore
```

### 3. Database Setup
```bash
# Update connection string in appsettings.json if needed
dotnet ef database update
```

### 4. Run the Application
```bash
dotnet run
```

The application will be available at `https://localhost:5001` or `http://localhost:5000`.

## 🗄️ Database Configuration

### SQL Server Setup
Update the connection string in `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=AcademiaAuditiva;Trusted_Connection=true;MultipleActiveResultSets=true"
  }
}
```

For Docker-based SQL Server (optional):
```bash
# If using SQL Server in Docker container
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourStrongPassword" -p 1433:1433 --name sqlserver -d mcr.microsoft.com/mssql/server:2019-latest
```

### User Authorization
After registering a new user, authorize them in the database:

```sql
-- Get user ID
SELECT * FROM [AcademiaAuditiva].[dbo].[AspNetUsers]

-- Authorize user
UPDATE [AcademiaAuditiva].[dbo].[AspNetUsers]
SET PhoneNumberConfirmed = 1, EmailConfirmed = 1
WHERE Id = 'USER_ID_HERE'
```

## 🏗️ Project Structure

```
AcademiaAuditiva/
├── AcademiaAuditiva/              # Main application
│   ├── Controllers/               # MVC Controllers
│   ├── Models/                    # Data models
│   ├── Views/                     # Razor views
│   ├── Services/                  # Business logic services
│   ├── Data/                      # Entity Framework context & migrations
│   ├── Resources/                 # Localization resources
│   ├── wwwroot/                   # Static files (CSS, JS, images)
│   └── Extensions/                # Extension methods
├── Documentation/                 # Project documentation
│   ├── Arquitetura.md            # Architecture overview (Portuguese)
│   ├── MapaDoProjeto.md          # Project roadmap (Portuguese)
│   ├── Pedagogia-Exercicios.md   # Exercise pedagogy (Portuguese)
│   └── Scrum_Backlog_AcademiaAuditiva.md # Development backlog
└── README.md                      # This file
```

## 🎮 Usage

1. **Register/Login** - Create an account or sign in
2. **Choose Exercise** - Select from various ear training exercises
3. **Configure Filters** - Customize difficulty and focus areas
4. **Practice** - Complete exercises and receive immediate feedback
5. **Track Progress** - Monitor your improvement on the dashboard
6. **Earn Badges** - Unlock achievements as you master skills

## 🔧 Development

### Running in Development Mode
```bash
dotnet run --environment Development
```

### Database Migrations
```bash
# Add new migration
dotnet ef migrations add MigrationName

# Update database
dotnet ef database update

# Remove last migration
dotnet ef migrations remove
```

### Adding New Exercises
1. Create exercise model in `Models/`
2. Add database entity and migration
3. Implement logic in `Services/MusicTheoryService.cs`
4. Add controller actions in `Controllers/ExerciseController.cs`
5. Create views in `Views/Exercise/`

## 📚 Additional Documentation

- [**Arquitetura.md**](Arquitetura.md) - Detailed architecture and design patterns
- [**MapaDoProjeto.md**](MapaDoProjeto.md) - Feature roadmap and exercise categories
- [**Pedagogia-Exercicios.md**](Pedagogia-Exercicios.md) - Educational methodology and exercise design
- [**FiltrosPorExercicio.md**](FiltrosPorExercicio.md) - Filter system documentation
- [**Scrum_Backlog_AcademiaAuditiva.md**](Scrum_Backlog_AcademiaAuditiva.md) - Development backlog and sprint planning

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

### Development Guidelines
- Follow ASP.NET Core conventions
- Add unit tests for new features
- Update documentation when adding new exercises
- Ensure localization for new UI elements

## 🚀 Deployment

### Azure Deployment
The application is configured for Azure deployment with:
- Azure Key Vault integration for secrets
- Azure Storage for file storage
- Azure SQL Database support

### Environment Configuration
Set the following environment variables:
- `AZURE_LOG_LEVEL` - Azure logging level
- `ManagedIdentityClientId` - Azure managed identity (production)

## 📄 License

This project is open source. Please check with the repository owner for specific licensing terms.

## 🙏 Acknowledgments

- [Tone.js](https://tonejs.github.io/) - Web Audio framework for music synthesis
- [Bootstrap](https://getbootstrap.com/) - CSS framework
- [ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/) - Web framework
- Music theory resources and educational methodology research

## 📞 Support

For support, questions, or feature requests, please open an issue on GitHub.

---

**Made with ❤️ for music education**