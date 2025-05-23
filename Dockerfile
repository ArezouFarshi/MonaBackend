# Use official .NET runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0

# Set the working directory inside the container
WORKDIR /app

# Copy the compiled output from your local machine into the container
COPY bin/Debug/net9.0/ .

# Set the command to run your application
ENTRYPOINT ["dotnet", "MonaBackendClean.dll"]
