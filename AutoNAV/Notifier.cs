using System;

namespace AutoNAV
{
    public enum NotifyLevel { Info, Success, Warning, Error, Result }

    // Tiny static shim so non-UI classes (SearchSetGenerator,
    // ClashTestGeneratorEngine, ClashGrouper) can post messages into the
    // MainWindow's in-app status panel without having to take a Window
    // reference.  MainWindow wires the Sink callback in its constructor.
    //
    // When Sink is null (e.g. unit tests or first construction) calls are
    // dropped silently — explicitly never falls back to a MessageBox so we
    // never re-introduce the pop-up dialogs the user wanted gone.
    public static class Notifier
    {
        public static Action<string, NotifyLevel, string> Sink;

        public static void Info(string message)    => Sink?.Invoke(message, NotifyLevel.Info,    null);
        public static void Success(string message) => Sink?.Invoke(message, NotifyLevel.Success, null);
        public static void Warning(string message) => Sink?.Invoke(message, NotifyLevel.Warning, null);
        public static void Error(string message)   => Sink?.Invoke(message, NotifyLevel.Error,   null);
        // Multi-line summary: title is one line, body is the indented block.
        public static void Result(string title, string body) => Sink?.Invoke(title, NotifyLevel.Result, body);
    }
}
