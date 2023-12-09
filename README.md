# AdaskoTheBeAsT.Interop.Threading

A C# library providing utilities for mutex handling, single-threaded apartment tasks, and task timeout management.

## Features

- **Mutex Helper**: Safely run code blocks within a named mutex, ensuring exclusive execution across processes.
- **Single Threaded Apartment Task**: Execute tasks in a single-threaded apartment (STA) state, useful for scenarios requiring STA, such as UI components.
- **Task Timeout Extension**: Add timeouts to tasks, throwing an exception if the task does not complete within the specified time frame.

## Installation

The library is available as a NuGet package: [AdaskoTheBeAsT.Interop.Threading](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Threading/)

## Usage

### Mutex Helper

Run a function within a global mutex:

```csharp
using AdaskoTheBeAsT.Interop.Threading;

var result = MutexHelper.RunInMutex("MyMutexName", () => {
    // Your code here
});
```

Run a function within a named mutex with a timeout:

```csharp
using AdaskoTheBeAsT.Interop.Threading;

var result = MutexHelper.RunInMutex("MyMutexName", TimeSpan.FromSeconds(30), () => {
    // Your code here
});
```

### Single Threaded Apartment Task

Run a function in a single-threaded apartment:

```csharp
var result = SingleThreadedApartmentTask.RunAsync(() => {
    // Your code here
}).Result;
```

Run a function in a single-threaded apartment with a timeout:

```csharp
var result = SingleThreadedApartmentTask.RunWithTimeoutAsync(TimeSpan.FromSeconds(30), () => {
    // Your code here
}).Result;
```

### Task Timeout Extension

Add a timeout to an existing task:

```csharp
var task = SomeAsyncOperation();
var result = task.TimeoutAfterAsync(TimeSpan.FromSeconds(30)).Result;
```