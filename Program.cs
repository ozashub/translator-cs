using System;

Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
global::Translator.App.Start(_ =>
{
    var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
    System.Threading.SynchronizationContext.SetSynchronizationContext(context);
    new Translator.App();
});
