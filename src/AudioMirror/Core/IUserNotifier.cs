namespace AudioMirror.Core;

/// <summary>Shows short, non-blocking messages to the user.</summary>
internal interface IUserNotifier
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}
