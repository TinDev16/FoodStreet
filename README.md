# Lap123
## Overview

Pato_Restaurant is a backend management system for a restaurant, focusing on inventory, products, suppliers, customers, and import/export operations. The project is built with Spring Boot and follows a layered architecture, aiming for scalability, maintainability, and high security.
This project is suitable for learning and practicing enterprise Java, Spring Boot, and Spring Security in a real-world restaurant management context.

## Features

- Manage Products, Suppliers, and Customers (Add, Edit, Delete)
- Inventory Import / Export management
- Role-based access control:
- Admin: full permissions
- Manager: management and reporting permissions
- Employee: limited operational permissions
- Authentication & Authorization with Spring Security
- RESTful APIs
- High security and clear separation of concerns

## Technologies Used:

- Spring Boot: 3.2.5
- Java: 17
- Build Tool: Maven
- Database: MySQL
- Security: Spring Security
- ORM: Spring Data JPA (Hibernate)

## Project Structure (Typical)
```bash
src/main/java
├── controller // REST Controllers
├── service // Business logic
├── repository // Data access layer (JPA)
├── entity // JPA Entities
├── security // Spring Security configuration
└── dto // Data Transfer Objects
```

## Prerequisites:

- Java Development Kit (JDK)
- Maven
- An IDE (e.g., IntelliJ IDEA, Eclipse)
- Database client or server (if applicable).

## Getting Started

1. Clone the repository:
```bash
git clone https://github.com/TinDev16/pato_restaurant
```

2. Configure the database:

```bash
spring.datasource.url=jdbc:mysql://localhost:3306/pato_restaurant
spring.datasource.username=yourUsername
spring.datasource.password=yourPassword
spring.jpa.hibernate.ddl-auto=update
spring.jpa.show-sql=true
spring.jpa.properties.hibernate.dialect=org.hibernate.dialect.MySQLDialect
```
3. Building the Project:
```bash
mvn clean install
```
4. Run the application
```bash
mvn spring-boot:run
```
The application will be available at:
```bash
http://localhost:8080
```
## Contributing

Contributions are welcome.
Fork the repository
Create a new branch
Commit your changes
Open a Pull Request

## License

This project is licensed under the MIT License.
