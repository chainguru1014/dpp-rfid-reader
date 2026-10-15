const path = require('path');

module.exports = {
  apps: [
    {
      name: 'rfid-reader-service',
      script: 'dotnet',
      args: 'RfidReaderService.dll',
      cwd: path.join(__dirname, 'src/RfidReaderService/bin/Release/net10.0/publish'),
      env: {
        ASPNETCORE_ENVIRONMENT: 'Production',
        ASPNETCORE_URLS: 'http://0.0.0.0:5284',
      },
      autorestart: true,
      max_restarts: 10,
      restart_delay: 3000,
    },
  ],
};
