# Null-guard

A tiny library that can create null-safe proxy objects for interfaces.

## Usage:

1. Create an interface
2. Add following attributes:
  - `[NullSafe]` for the interface itself
  - `[NeverNull]` or `[CanBeNull]` for properties, methods and method arguments (if none is used `NeverNull` is implied)
3. Call `NullSafety<ITargetInterface>.Guard(instance)` function to get a proxy for `instance` with null-checks

Optionally, `NullSafety<ITargetInterface>.Prepare()` can be called before first use to generate the proxy class in advance.

## Example
```cs
[NullSafe]
public interface IExample {
    
    [CanBeNull]
    string Process([NeverNull] string arg);
}
```
```cs
public class Example : IExample {

    string Process(string arg) => arg.ToLower();
}
```
```cs
var guarded = NullSafety<IExample>.Guard(new Example());
Console.WriteLine(guarded.Process(null)); // throws NullSafetyViolationException
```
