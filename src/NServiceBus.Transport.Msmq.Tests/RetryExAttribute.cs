// Copyright (c) Charlie Poole, Rob Prouse and Contributors. MIT License - see LICENSE.txt

using System;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

/// <summary>
/// Specifies that a test method should be rerun on failure up to the specified 
/// maximum number of times.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class RetryExAttribute : NUnitAttribute, IRepeatTest
{
    readonly int _tryCount;
    readonly Type[] _exceptions;

    /// <summary>
    /// Construct a <see cref="RetryAttribute" />
    /// </summary>
    /// <param name="tryCount">The maximum number of times the test should be run if it fails</param>
    public RetryExAttribute(int tryCount, params Type[] exceptions)
    {
        _tryCount = tryCount;
        _exceptions = exceptions;
    }

    #region IRepeatTest Members

    /// <summary>
    /// Wrap a command and return the result.
    /// </summary>
    /// <param name="command">The command to be wrapped</param>
    /// <returns>The wrapped command</returns>
    public TestCommand Wrap(TestCommand command)
    {
        return new RetryCommand(command, _tryCount, _exceptions);
    }

    #endregion

    #region Nested RetryCommand Class

    /// <summary>
    /// The test command for the <see cref="RetryAttribute"/>
    /// </summary>
    public class RetryCommand : DelegatingTestCommand
    {
        readonly int _tryCount;
        readonly Type[] _exceptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryCommand"/> class.
        /// </summary>
        /// <param name="innerCommand">The inner command.</param>
        /// <param name="tryCount">The maximum number of repetitions</param>
        public RetryCommand(TestCommand innerCommand, int tryCount, Type[] exceptions)
            : base(innerCommand)
        {
            _tryCount = tryCount;
            _exceptions = exceptions;
        }

        /// <summary>
        /// Runs the test, saving a TestResult in the supplied TestExecutionContext.
        /// </summary>
        /// <param name="context">The context in which the test should run.</param>
        /// <returns>A TestResult</returns>
        public override TestResult Execute(TestExecutionContext context)
        {
            int count = _tryCount;

            while (count-- > 0)
            {
                bool bla = false;
                try
                {
                    context.CurrentResult = innerCommand.Execute(context);
                }
                // Commands are supposed to catch exceptions, but some don't
                // and we want to look at restructuring the API in the future.
                catch (Exception ex) //when (_exceptions.Contains(ex.GetType()))
                {
                    bla = _exceptions.Contains(ex.InnerException.GetType());

                    if (context.CurrentResult == null)
                    {
                        context.CurrentResult = context.CurrentTest.MakeTestResult();
                    }
                    context.CurrentResult.RecordException(ex);
                }

                if (context.CurrentResult.ResultState != ResultState.Failure && !bla)
                {
                    break;
                }

                // Clear result for retry
                if (count > 0)
                {
                    context.CurrentResult = context.CurrentTest.MakeTestResult();
                    context.CurrentRepeatCount++; // increment Retry count for next iteration. will only happen if we are guaranteed another iteration
                }
            }

            return context.CurrentResult;
        }
    }

    #endregion
}