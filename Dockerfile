# Use the .NET SDK image to build the application
# Make sure the SDK version matches your project's target framework (.NET 9 in this case)
# Check Docker Hub for the latest available .NET 9 SDK tags if needed
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# Copy the project file and restore dependencies
COPY *.csproj .
RUN dotnet restore

# Copy the rest of the application code
COPY . .

# Publish the application to the 'app' folder using Release configuration
# Use a self-contained publish or ensure the base runtime image has the right framework
RUN dotnet publish -c Release -o /app --no-restore

# Use the ASP.NET runtime image to run the application
# This image is smaller than the SDK image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# --- Volume Mount Point for SQLite ---
# Fly.io typically mounts volumes at /data. We will store the DB here.
# Create the directory (though Fly might create it on volume mount)
RUN mkdir /data

# Copy the published application from the build stage
COPY --from=build /app .

# Set the ASPNETCORE_URLS environment variable to listen on port 8080
# Fly.io's internal proxy expects apps to listen on 8080 by default
ENV ASPNETCORE_URLS=http://+:8080

# Define the entry point for the container
ENTRYPOINT ["dotnet", "TicTacToeBlazor.dll"]