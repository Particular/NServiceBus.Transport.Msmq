# USAGE:

# To delete the endpoint and all the subqueues, use:
# DeleteQueuesForEndpoint -EndpointName "myendpoint" 

# To delete a single queue such as Audit or Error, use:
# DeleteQueue -QueueName "error"

Set-StrictMode -Version 2.0

Add-Type -AssemblyName System.Messaging

Function DeleteQueuesForEndpoint
{
    param(
        [Parameter(Mandatory=$true)]
        [string] $endpointName
    )

    # main queue
    DeleteQueue $endpointName

    # timeout queue
    DeleteQueue ($endpointName + ".timeouts")

    # timeout dispatcher queue
    DeleteQueue ($endpointName + ".timeoutsdispatcher")

    # retries queue
    # TODO: Only required in Versions 5 and below
    DeleteQueue ($endpointName + ".retries")
}

Function DeleteQueue
{
    param(
        [Parameter(Mandatory=$true)]
        [string] $queueName
    )

    $queuePath = '{0}\private$\{1}'-f [System.Environment]::MachineName, $queueName
    if ([System.Messaging.MessageQueue]::Exists($queuePath))
    {
        [System.Messaging.MessageQueue]::Delete($queuePath)
    }
}
