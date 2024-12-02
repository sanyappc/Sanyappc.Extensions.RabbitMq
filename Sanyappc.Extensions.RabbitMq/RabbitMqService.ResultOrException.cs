namespace Sanyappc.Extensions.RabbitMq
{
    internal partial class RabbitMqService
    {
        private record ResultOrException<T>
        {
            private readonly T? result;
            private readonly Exception? exception;

            public ResultOrException(T result)
            {
                this.result = result;
            }

            public ResultOrException(Exception exception)
            {
                this.exception = exception;
            }

            public T Result => result ?? throw (exception ?? new InvalidOperationException());
        }
    }
}
