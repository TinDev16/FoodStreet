# Lap123
## Overview

FoodStreet is a multilingual food street tour system designed to help tourists explore culinary areas through a mobile application with GPS, geofencing, and audio narration, along with a web-based management system for shop owners and administrators.

The project is built using C# and the .NET ecosystem, including .NET MAUI for mobile and ASP.NET Core for Web & API, following a clean, layered architecture to ensure scalability, maintainability, and offline-first capability.

This project is suitable for learning and practicing .NET MAUI, ASP.NET Core, RESTful APIs, SQLite, GPS tracking, and geofencing in a real-world smart tourism context.

## Features

Mobile Application (Tourist)
- Display food street POIs (Points of Interest) on a map
- Show real-time user location
- GPS tracking with battery optimization
Geofencing:
- Trigger audio narration when entering or approaching a POI
- Priority-based POI selection
- Audio narration system:
- Audio file playback or TTS fallback
Queue management
- Cooldown & debounce to prevent duplicate playback
- Background location tracking
- Multilingual support
- Offline-first architecture using SQLite
- Automatic data synchronization when WiFi is available

Web Management System (Shop Owner & Admin)
Authentication & role-based access control:
- Admin: full system management
- Shop Owner: manage own POIs and content
Manage POIs:
- Name, description, images
- Audio narration
- Translations
Tour management
View statistics:
- POI views
- Audio play counts
Content synchronization for mobile app

## Data Analytics (Anonymous)
- User movement paths (anonymous)
- Top most-listened POIs
- Average listening time per POI
- Heatmap-ready location logs

## Technologies Used:

Mobile
- .NET MAUI
- C#
- GPS & Background Services
- Text-to-Speech (TTS)
- SQLite (offline storage)
  
Backend & Web
- ASP.NET Core Web API
- ASP.NET Core MVC
- Entity Framework Core
- SQLite
- RESTful APIs
  
General
- .NET 8+
- Dependency Injection
- Clean / Layered Architecture

## Project Structure (Typical)
```bash
FoodStreet
├── FoodStreet.Mobile        // .NET MAUI mobile app
│   ├── Views
│   ├── ViewModels
│   ├── Services
│   ├── Models
│   └── Data (SQLite)
│
├── FoodStreet.API           // ASP.NET Core Web API
│   ├── Controllers
│   ├── Services
│   ├── Repositories
│   └── Models
│
├── FoodStreet.Web           // ASP.NET Core MVC (Admin & Shop)
│   ├── Controllers
│   ├── Views
│   ├── Models
│   └── wwwroot
│
└── README.md
```

## System Architecture
Mobile App: Offline-first, GPS tracking, geofencing, audio narration
Backend API: Central data provider, logging, synchronization
Web Management: Content & system administration
Database:
- SQLite (local & server)
- Sync via WiFi

## Prerequisites:

- NET SDK (8.0 or later)
- Visual Studio 2022+
- Android SDK (for mobile testing)
- SQLite
- Internet connection (for sync & map services)

## Getting Started

1. Clone the repository:
```bash
git clone https://github.com/your-username/FoodStreet
```

2. Run Backend API

```bash
cd FoodStreet.API
dotnet restore
dotnet run
```
3. Run Web Management
```bash
cd FoodStreet.Web
dotnet restore
dotnet run
```
4. Run Mobile App
- Open FoodStreet.Mobile in Visual Studio
- Select Android Emulator / Device
- Run the project

## Learning Objectives

- Build a real mobile application using .NET MAUI
- Apply GPS & Geofencing in real-world scenarios
- Design offline-first mobile architecture
- Implement RESTful APIs with ASP.NET Core
- Practice clean architecture & separation of concerns
- Develop a full-stack .NET system

## Contributing

Contributions are welcome.
- Fork the repository
- Create a new branch
- Commit your changes
- Open a Pull Request

## License

This project is licensed under the MIT License.
