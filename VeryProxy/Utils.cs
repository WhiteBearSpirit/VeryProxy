using System;
using System.Threading.Tasks;

namespace VeryProxy
{
    public static class Utils
    {
        public const string AddressPortRegEx = @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?):\d{1,5}\b";
        public const string AddressPortPlusRegEx = @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?):\d{1,5}[\+-]{0,10}";
        

        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, TimeSpan timeout)
        {

            var delayCancellationTokenSource = new System.Threading.CancellationTokenSource();
            var delayTask = Task.Delay(timeout, delayCancellationTokenSource.Token);
            var completedTask = await Task.WhenAny(task, delayTask);
            if (completedTask == task)
            {
                delayCancellationTokenSource.Cancel();
                return task.GetAwaiter().GetResult();
            }
            else
            {
                throw new TimeoutException("The operation has timed out.");
            }
        }       

    }

    public class OutOfProxyException : Exception
    {
        public OutOfProxyException() { }
        public OutOfProxyException(string message) : base(message) { }
    }

}
