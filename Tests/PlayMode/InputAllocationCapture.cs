#if INPUTSYSTEM_SUPPORT && UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace AlicizaX.Input.Tests
{
    /// <summary>
    /// 基于 Unity Profiler 的 GC.Alloc 采样器。Unity 2022.3 的 Mono 不支持
    /// GC.GetAllocatedBytesForCurrentThread（恒为 0），因此用 Profiler 原始帧数据测量精确字节数。
    /// </summary>
    internal static class InputAllocationCapture
    {
        private const int MaxProfilerBytes = 256 * 1024 * 1024;
        private static int _sequence;

        internal static IEnumerator Measure(string name, Action action, Action<long, int> verify = null)
        {
            bool wasEnabled = ProfilerDriver.enabled;
            bool wasProfileEditor = ProfilerDriver.profileEditor;
            bool wasCpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
            bool wasMemory = ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory);
            int previousBudget = Profiler.maxUsedMemory;
            var clock = new Stopwatch();
            string marker = "InputAudit.Measure." + (++_sequence);

            Profiler.maxUsedMemory = MaxProfilerBytes;
            ProfilerDriver.profileEditor = !UnityEngine.Application.isPlaying;
            ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
            ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, true);
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.enabled = true;
            try
            {
                RuntimeHelpers.PrepareDelegate(action);
                action.Method.MethodHandle.GetFunctionPointer();
                yield return null;

                int firstFrame = ProfilerDriver.lastFrameIndex;
                Profiler.BeginSample(marker);
                try
                {
                    clock.Start();
                    action();
                }
                finally
                {
                    clock.Stop();
                    Profiler.EndSample();
                }

                bool found = false;
                long bytes = 0;
                int allocations = 0;
                double deadline = EditorApplication.timeSinceStartup + 5d;
                while (!found && EditorApplication.timeSinceStartup < deadline)
                {
                    InternalEditorUtility.RepaintAllViews();
                    yield return null;
                    for (int frame = Math.Max(firstFrame, ProfilerDriver.firstFrameIndex);
                         frame <= ProfilerDriver.lastFrameIndex && !found;
                         frame++)
                    {
                        using var data = ProfilerDriver.GetRawFrameDataView(frame, 0);
                        if (!data.valid)
                        {
                            continue;
                        }

                        int markerId = data.GetMarkerId(marker);
                        int allocationId = data.GetMarkerId("GC.Alloc");
                        for (int i = 0; i < data.sampleCount; i++)
                        {
                            if (data.GetSampleMarkerId(i) != markerId)
                            {
                                continue;
                            }

                            found = true;
                            int end = i + data.GetSampleChildrenCountRecursive(i);
                            for (int child = i + 1; child <= end; child++)
                            {
                                if (data.GetSampleMarkerId(child) != allocationId)
                                {
                                    continue;
                                }

                                bytes += data.GetSampleMetadataAsLong(child, 0);
                                allocations++;
                            }

                            break;
                        }
                    }
                }

                Assert.That(found, Is.True, "Profiler did not capture the workload marker for '" + name + "'.");
                string line = "INPUT_ALLOC," + name + "," + bytes + "," + allocations + "," + clock.Elapsed.TotalMilliseconds.ToString("F4");
                TestContext.WriteLine(line);
                UnityEngine.Debug.Log(line);
                verify?.Invoke(bytes, allocations);
            }
            finally
            {
                ProfilerDriver.enabled = false;
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.enabled = wasEnabled;
                ProfilerDriver.profileEditor = wasProfileEditor;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, wasCpu);
                ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, wasMemory);
                Profiler.maxUsedMemory = previousBudget;
            }
        }
    }
}
#endif
