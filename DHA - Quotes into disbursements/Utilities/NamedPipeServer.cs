using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace DHA.DSTC.WPF.Utilities
{
    /// <summary>
    /// Named pipe server for inter-process communication between application instances
    /// </summary>
    public class NamedPipeServer : IDisposable
    {
        private readonly string _pipeName;
        private NamedPipeServerStream _pipeServer;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _serverTask;
        private bool _isDisposed = false;

        /// <summary>
        /// Event raised when a message is received from a client
        /// </summary>
        public event Action<string> MessageReceived;

        /// <summary>
        /// Creates a new named pipe server
        /// </summary>
        /// <param name="pipeName">Name of the pipe (without \\\\.\\pipe\\ prefix)</param>
        public NamedPipeServer(string pipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
                throw new ArgumentException("Pipe name cannot be null or empty", nameof(pipeName));

            _pipeName = pipeName;
            _cancellationTokenSource = new CancellationTokenSource();

            System.Diagnostics.Debug.WriteLine($"NamedPipeServer: Created for pipe '{_pipeName}'");
        }

        /// <summary>
        /// Starts the pipe server to listen for incoming connections
        /// </summary>
        public void Start()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(NamedPipeServer));

            if (_serverTask != null && !_serverTask.IsCompleted)
            {
                System.Diagnostics.Debug.WriteLine("NamedPipeServer: Server is already running");
                return;
            }

            System.Diagnostics.Debug.WriteLine("NamedPipeServer: Starting server");
            _serverTask = Task.Run(async () => await RunServerAsync());
        }

        /// <summary>
        /// Stops the pipe server
        /// </summary>
        public void Stop()
        {
            if (_isDisposed)
                return;

            System.Diagnostics.Debug.WriteLine("NamedPipeServer: Stopping server");

            try
            {
                _cancellationTokenSource?.Cancel();

                // Close the current pipe to unblock WaitForConnectionAsync
                _pipeServer?.Close();

                // Wait for server task to complete with timeout
                if (_serverTask != null)
                {
                    if (!_serverTask.Wait(TimeSpan.FromSeconds(3)))
                    {
                        System.Diagnostics.Debug.WriteLine("NamedPipeServer: Server task did not complete within timeout");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NamedPipeServer: Error stopping server: {ex.Message}");
            }

            System.Diagnostics.Debug.WriteLine("NamedPipeServer: Server stopped");
        }

        /// <summary>
        /// Main server loop that handles incoming connections
        /// </summary>
        private async Task RunServerAsync()
        {
            System.Diagnostics.Debug.WriteLine("NamedPipeServer: Server loop started");

            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                NamedPipeServerStream currentPipeServer = null;

                try
                {
                    // Create a new pipe server instance for each connection
                    currentPipeServer = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.In,
                        1, // Maximum number of server instances
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    _pipeServer = currentPipeServer; // Store reference for potential cancellation

                    System.Diagnostics.Debug.WriteLine("NamedPipeServer: Waiting for client connection");

                    // Wait for a client to connect
                    await _pipeServer.WaitForConnectionAsync(_cancellationTokenSource.Token);

                    System.Diagnostics.Debug.WriteLine("NamedPipeServer: Client connected, reading message");

                    // Read the message from the client
                    using (var reader = new StreamReader(_pipeServer))
                    {
                        var message = await reader.ReadLineAsync();

                        if (!string.IsNullOrEmpty(message))
                        {
                            System.Diagnostics.Debug.WriteLine($"NamedPipeServer: Received message: '{message}'");

                            // Raise the event on a separate task to avoid blocking the server loop
                            Task.Run(() =>
                            {
                                try
                                {
                                    MessageReceived?.Invoke(message);
                                }
                                catch (Exception eventEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"NamedPipeServer: Error in MessageReceived event handler: {eventEx.Message}");
                                }
                            });
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("NamedPipeServer: Received empty message");
                        }
                    }

                    System.Diagnostics.Debug.WriteLine("NamedPipeServer: Message processed, disconnecting client");
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("NamedPipeServer: Server operation cancelled");
                    break; // Expected when cancellation is requested
                }
                catch (ObjectDisposedException)
                {
                    System.Diagnostics.Debug.WriteLine("NamedPipeServer: Pipe disposed, stopping server");
                    break; // Expected when disposed
                }
                catch (IOException ioEx)
                {
                    System.Diagnostics.Debug.WriteLine($"NamedPipeServer: IO error (client likely disconnected): {ioEx.Message}");
                    // This is common when clients disconnect abruptly, continue running
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"NamedPipeServer: Unexpected error in server loop: {ex.Message}");

                    // Brief delay before restarting to avoid tight error loops
                    try
                    {
                        await Task.Delay(1000, _cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                finally
                {
                    // Clean up the current pipe instance
                    try
                    {
                        currentPipeServer?.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"NamedPipeServer: Error disposing pipe: {disposeEx.Message}");
                    }

                    if (ReferenceEquals(_pipeServer, currentPipeServer))
                    {
                        _pipeServer = null;
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine("NamedPipeServer: Server loop ended");
        }

        /// <summary>
        /// Gets whether the server is currently running
        /// </summary>
        public bool IsRunning
        {
            get
            {
                return _serverTask != null &&
                       !_serverTask.IsCompleted &&
                       !_cancellationTokenSource.Token.IsCancellationRequested;
            }
        }

        /// <summary>
        /// Disposes of the pipe server and associated resources
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            System.Diagnostics.Debug.WriteLine("NamedPipeServer: Disposing");

            _isDisposed = true;

            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NamedPipeServer: Error during dispose: {ex.Message}");
            }
            finally
            {
                try
                {
                    _cancellationTokenSource?.Dispose();
                    _pipeServer?.Dispose();
                    _serverTask?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"NamedPipeServer: Error disposing resources: {ex.Message}");
                }

                _cancellationTokenSource = null;
                _pipeServer = null;
                _serverTask = null;
            }

            System.Diagnostics.Debug.WriteLine("NamedPipeServer: Disposed");
        }

        /// <summary>
        /// Finalizer to ensure resources are cleaned up
        /// </summary>
        ~NamedPipeServer()
        {
            Dispose();
        }
    }
}