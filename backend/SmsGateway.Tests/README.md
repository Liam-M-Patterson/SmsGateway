# SmsGateway.Tests

This project contains unit tests for the SmsGateway backend.

## Prerequisites

- .NET SDK installed on your machine.
- Ensure all dependencies are restored before running the tests.

## Running the Tests

1. Navigate to the project directory:
    ```bash
    cd ./backend/SmsGateway.Tests
    ```

2. Restore dependencies:
    ```bash
    dotnet restore
    ```

3. Run the tests:
    ```bash
    dotnet test
    ```

## Notes

- Ensure the backend project is up-to-date to avoid compatibility issues.
- Test results will be displayed in the console after execution.
- For detailed logs, use the `--logger` option:
    ```bash
    dotnet test --logger "console;verbosity=detailed"
    ```