using System;

namespace NexSync.Desktop.Auth;

public sealed class UnauthorizedException(string message) : Exception(message);

public sealed class DeviceRevokedException(string message = "This device has been revoked.") : Exception(message);
