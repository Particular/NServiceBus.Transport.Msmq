using System;
using System.Collections.Generic;
using System.Text;

namespace System.Messaging
{
    public class MessageQueuePermission
    {
        MessageQueuePermissionAccess _administer;
        string _queueName;

        public MessageQueuePermission(MessageQueuePermissionAccess administer, string queueName)
        {
            _administer = administer;
            _queueName = queueName;
        }

        public void Demand()
        {
           
        }
    }
}
