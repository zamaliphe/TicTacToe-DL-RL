﻿/*
 * Idea: Create buffers for the network weights and keep X different networks in GPU memory
 * For each network we also add Y different inputs and so we run X*Y forward passes in one call
 * The inputs are changed on each call but weights only change after evolution
 * TODOs: Don't resend untrainable weights, don't resend weights for every input
 * Copy stuff to local memory
 */
 using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cloo;
using System.IO;
using System.Threading;
using System.Threading.Channels;

namespace TicTacToe_DL_RL
{
    static class OpenCL
    {
        public static List<Channel<Job>> ResponseChannels;
        public static Channel<Job> InputChannel = Channel.CreateUnbounded<Job>();
        public static List<ChannelWriter<Job>> writers;
        public static ChannelReader<Job> reader; 

        // for input layer
        public static List<float> input = new List<float>();
        public static List<float> firstConvFilterWeights = new List<float>();

        // for residual tower
        public static List<float> convFilterWeights = new List<float>();

        // for policy layer
        public static List<float> convWeightsPolicy = new List<float>();
        public static List<float> BNMeansPolicy = new List<float>();
        public static List<float> BNStddevPolicy = new List<float>();
        public static List<float> BNBetaPolicy = new List<float>();
        public static List<float> BNGammaPolicy = new List<float>();
        public static List<float> policyConnectionWeights = new List<float>();
        public static List<float> policyBiases = new List<float>();

        // for value layer
        public static List<float> convWeightsValue1 = new List<float>();
        public static List<float> convWeightsValue2 = new List<float>();
        public static List<float> BNMeansValue = new List<float>();
        public static List<float> BNStddevValue = new List<float>();
        public static List<float> BNBetaValue = new List<float>();
        public static List<float> BNGammaValue = new List<float>();
        public static List<float> valueConnectionWeights = new List<float>();
        public static List<float> valueBiases = new List<float>();
        public static List<float> valueBiasLast = new List<float>();

        // for all layers
        public static List<float> BNMeans = new List<float>();
        public static List<float> BNStddev = new List<float>();

        public static List<float> BNBetas = new List<float>();
        public static List<float> BNGammas = new List<float>();

        // output of NN
        public static float[] output = new float[(Params.MAX_KERNEL_EXECUTIONS) *26];
        public static List<int> networkIndex = new List<int>();

        // opencl buffers
        static private ComputeBuffer<float> CB_input;
        static private ComputeBuffer<float> CB_firstConvFilterWeights;
        static private ComputeBuffer<float> CB_BNMeans;
        static private ComputeBuffer<float> CB_BNStddev;
        static private ComputeBuffer<float> CB_BNBetas;
        static private ComputeBuffer<float> CB_BNGammas;

        static private ComputeBuffer<float> CB_convWeightsValue1;
        static private ComputeBuffer<float> CB_convWeightsValue2;
        static private ComputeBuffer<float> CB_BNMeansValue;
        static private ComputeBuffer<float> CB_BNStddevValue;
        static private ComputeBuffer<float> CB_BNBetaValue;
        static private ComputeBuffer<float> CB_BNGammaValue;
        static private ComputeBuffer<float> CB_valueConnectionWeights;
        static private ComputeBuffer<float> CB_valueBiases;
        static private ComputeBuffer<float> CB_valueBiasLast;

        static private ComputeBuffer<float> CB_convWeightsPolicy;
        static private ComputeBuffer<float> CB_BNMeansPolicy;
        static private ComputeBuffer<float> CB_BNStddevPolicy;
        static private ComputeBuffer<float> CB_BNBetaPolicy;
        static private ComputeBuffer<float> CB_BNGammaPolicy;
        static private ComputeBuffer<float> CB_policyConnectionWeights;
        static private ComputeBuffer<float> CB_policyBiases;

        static private ComputeBuffer<float> CB_convFilterWeights;
        static private ComputeBuffer<float> CB_output;

        static private ComputeBuffer<int> CB_networkIndex;

        // opencl stuff
        static private ComputeProgram program;
        static private ComputePlatform platform;
        static private ComputeContext context;
        static private ComputeContextPropertyList properties;
        static private ComputeKernel kernel;

        public static void Init(int maxChannels)
        {
            CompileKernel();
            ResponseChannels = new List<Channel<Job>>();
            writers = new List<ChannelWriter<Job>>();
            for (int i = 0; i < maxChannels; ++i)
            {
                ResponseChannels.Add(Channel.CreateUnbounded<Job>());
                writers.Add(ResponseChannels[i].Writer);
            }
            reader = InputChannel.Reader;
        }

        public static void Run()
        {
            while (true)
            {
                input.Clear();
                networkIndex.Clear();
                for (int i = 0; i < Params.MAX_KERNEL_EXECUTIONS; ++i)
                {
                    //Task t = Consume(InputChannel);
                    Task<Job> task = Consume(InputChannel).AsTask();
                    try
                    {
                        Job job = task.Result;
                        //var result = Task.Run(async () => { return await Consume(InputChannel); }).Wait(100);
                        if (job != null)
                        {
                            for (int j = 0; j < job.input.Count; ++j)
                            {
                                input.Add(job.input[j]);
                            }
                            networkIndex.Add(job.globalID);
                        }
                    }
                    catch (Exception e) { 
                        //Console.WriteLine("Timeout on waiting for input to openCL channel (intended behaviour)");
                    }
                }
                RunKernels();

                int outputCount = 0;
                for(int i = 0; i < networkIndex.Count; ++i)
                {
                    Job job = new Job();
                    for (int j = 0; j < 26; ++j)
                    {
                        job.output.Add(output[outputCount]);
                        outputCount++;
                    }
                    writers[networkIndex[i]].TryWrite(job);
                }
            }
        }
        private static async ValueTask<Job> Consume(ChannelReader<Job> c)
        {
            try
            {
                TimeSpan timeout = TimeSpan.FromMilliseconds(1);

                using (var cts = new CancellationTokenSource(timeout))
                {
                    return await c.ReadAsync(cts.Token);
                }
            }
            catch (TaskCanceledException e) {
                return null;
            }
        }
        public static void CompileKernel()
        {
            platform = ComputePlatform.Platforms[0]; // todo find amd gpus..
            IList<ComputeDevice> devices;
            devices = new List<ComputeDevice>();

            object[] availableDevices = new object[platform.Devices.Count];
            for (int i = 0; i < availableDevices.Length; i++)
                availableDevices[i] = platform.Devices[i].Name;

            properties = new ComputeContextPropertyList(platform);
            devices.Add(platform.Devices[0]);
            context = new ComputeContext(devices, properties, null, IntPtr.Zero);

            // The output buffer doesn't need any data from the host. Only its size is specified res.length.
            CB_output = new ComputeBuffer<float>(context, ComputeMemoryFlags.WriteOnly | ComputeMemoryFlags.CopyHostPointer, output);

            // Create and build the opencl program.
            StreamReader streamReader = new StreamReader("../../NeuralNetwork.cl");
            string openClCode = streamReader.ReadToEnd();
            streamReader.Close();
            program = new ComputeProgram(context, openClCode);
            //string log = program.GetBuildLog(devices[0]);
            ComputeProgramBuildStatus error = program.GetBuildStatus(context.Devices[0]);
            try
            {
                program.Build(null, null, null, IntPtr.Zero);
            }
            catch
            {
                Console.WriteLine(program.GetBuildLog(devices[0]));
                throw;
            }
            // Create the kernel function and set its arguments.
            kernel = program.CreateKernel("NN");
        }

        public static void EnqueueWeights(NeuralNetwork nn)
        {
            firstConvFilterWeights.AddRange(nn.firstConvFilterWeights);

            // for residual tower
            convFilterWeights.AddRange(nn.convFilterWeights);

            // for policy layer
            convWeightsPolicy.AddRange(nn.convWeightsPolicy);
            BNMeansPolicy.AddRange(nn.BNMeansPolicy);
            BNStddevPolicy.AddRange(nn.BNStddevPolicy);
            BNBetaPolicy.AddRange(nn.BNBetaPolicy);
            BNGammaPolicy.AddRange(nn.BNGammaPolicy);
            policyConnectionWeights.AddRange(nn.policyConnectionWeights);
            policyBiases.AddRange(nn.policyBiases);

            // for value layer
            convWeightsValue1.AddRange(nn.convWeightsValue1);
            convWeightsValue2.AddRange(nn.convWeightsValue2);
            BNMeansValue.AddRange(nn.BNMeansValue);
            BNStddevValue.AddRange(nn.BNStddevValue);
            BNBetaValue.AddRange(nn.BNBetaValue);
            BNGammaValue.AddRange(nn.BNGammaValue);
            valueConnectionWeights.AddRange(nn.valueConnectionWeights);
            valueBiases.AddRange(nn.valueBiases);
            valueBiasLast.AddRange(nn.valueBiasLast);

            // for all layers
            BNMeans.AddRange(nn.BNMeans);
            BNStddev.AddRange(nn.BNStddev);

            BNBetas.AddRange(nn.BNBetas);
            BNGammas.AddRange(nn.BNGammas);
        }
        public static void ClearWeights()
        {
            firstConvFilterWeights.Clear();

            // for residual tower
            convFilterWeights.Clear();

            // for policy layer
            convWeightsPolicy.Clear();
            BNMeansPolicy.Clear();
            BNStddevPolicy.Clear();
            BNBetaPolicy.Clear();
            BNGammaPolicy.Clear();
            policyConnectionWeights.Clear();
            policyBiases.Clear();

            // for value layer
            convWeightsValue1.Clear();
            convWeightsValue2.Clear();
            BNMeansValue.Clear();
            BNStddevValue.Clear();
            BNBetaValue.Clear();
            BNGammaValue.Clear();
            valueConnectionWeights.Clear();
            valueBiases.Clear();
            valueBiasLast.Clear();

            // for all layers
            BNMeans.Clear();
            BNStddev.Clear();

            BNBetas.Clear();
            BNGammas.Clear();
        }
        public static void RunKernels()
        {
            Console.WriteLine("Kernel executions starting...");
            CB_input = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, input.ToArray());
            CB_networkIndex = new ComputeBuffer<int>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, networkIndex.ToArray());

            try
            {
                kernel.SetMemoryArgument(0, CB_input);
                kernel.SetMemoryArgument(24, CB_networkIndex);

                // Create the event wait list. An event list is not really needed for this example but it is important to see how it works.
                // Note that events (like everything else) consume OpenCL resources and creating a lot of them may slow down execution.
                // For this reason their use should be avoided if possible.
                ComputeEventList eventList = new ComputeEventList();

                // Create the command queue. This is used to control kernel execution and manage read/write/copy operations.
                ComputeCommandQueue commands = new ComputeCommandQueue(context, context.Devices[0], ComputeCommandQueueFlags.None);

                // Execute the kernel "count" times. After this call returns, "eventList" will contain an event associated with this command.
                // If eventList == null or typeof(eventList) == ReadOnlyCollection<ComputeEventBase>, a new event will not be created.

                commands.Execute(kernel, null, new long[] { networkIndex.Count }, null, eventList);

                // Read back the results. If the command-queue has out-of-order execution enabled (default is off), ReadFromBuffer 
                // will not execute until any previous events in eventList (in our case only eventList[0]) are marked as complete 
                // by OpenCL. By default the command-queue will execute the commands in the same order as they are issued from the host.
                // eventList will contain two events after this method returns.
                commands.ReadFromBuffer(CB_output, ref output, false, eventList);

                // A blocking "ReadFromBuffer" (if 3rd argument is true) will wait for itself and any previous commands
                // in the command queue or eventList to finish execution. Otherwise an explicit wait for all the opencl commands 
                // to finish has to be issued before "arrC" can be used. 
                // This explicit synchronization can be achieved in two ways:

                // 1) Wait for the events in the list to finish,
                //eventList.Wait();

                // 2) Or simply use
                commands.Finish();
                CB_input.Dispose();
                commands.Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        // TODO: remove duplicate
        public static void CreateNetworkWeightBuffers()
        {
            // Create the input buffers and fill them with data from the arrays.
            // Access modifiers should match those in a kernel.
            // CopyHostPointer means the buffer should be filled with the data provided in the last argument.
            // opencl buffers
            CB_firstConvFilterWeights = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, firstConvFilterWeights.ToArray());

            CB_BNMeans = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNMeans.ToArray());
            CB_BNStddev = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNStddev.ToArray());
            CB_BNBetas = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNBetas.ToArray());
            CB_BNGammas = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNGammas.ToArray());

            CB_convWeightsValue1 = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, convWeightsValue1.ToArray());
            CB_convWeightsValue2 = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, convWeightsValue2.ToArray());
            CB_BNMeansValue = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNMeansValue.ToArray());
            CB_BNStddevValue = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNStddevValue.ToArray());
            CB_BNBetaValue = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNBetaValue.ToArray());
            CB_BNGammaValue = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNGammaValue.ToArray());
            CB_valueConnectionWeights = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, valueConnectionWeights.ToArray());
            CB_valueBiases = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, valueBiases.ToArray());
            CB_valueBiasLast = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, valueBiasLast.ToArray());

            CB_convWeightsPolicy = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, convWeightsPolicy.ToArray());
            CB_BNMeansPolicy = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNMeansPolicy.ToArray());
            CB_BNStddevPolicy = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNStddevPolicy.ToArray());
            CB_BNBetaPolicy = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNBetaPolicy.ToArray());
            CB_BNGammaPolicy = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, BNGammaPolicy.ToArray());
            CB_policyConnectionWeights = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, policyConnectionWeights.ToArray());
            CB_policyBiases = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, policyBiases.ToArray());
            CB_convFilterWeights = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, convFilterWeights.ToArray());

            try
            {
                kernel.SetMemoryArgument(1, CB_firstConvFilterWeights);

                kernel.SetMemoryArgument(2, CB_BNMeans);
                kernel.SetMemoryArgument(3, CB_BNStddev);
                kernel.SetMemoryArgument(4, CB_BNBetas);
                kernel.SetMemoryArgument(5, CB_BNGammas);

                kernel.SetMemoryArgument(6, CB_convWeightsValue1);
                kernel.SetMemoryArgument(7, CB_convWeightsValue2);
                kernel.SetMemoryArgument(8, CB_BNMeansValue);
                kernel.SetMemoryArgument(9, CB_BNStddevValue);
                kernel.SetMemoryArgument(10, CB_BNBetaValue);
                kernel.SetMemoryArgument(11, CB_BNGammaValue);
                kernel.SetMemoryArgument(12, CB_valueConnectionWeights);
                kernel.SetMemoryArgument(13, CB_valueBiases);
                kernel.SetMemoryArgument(14, CB_valueBiasLast);

                kernel.SetMemoryArgument(15, CB_convWeightsPolicy);
                kernel.SetMemoryArgument(16, CB_BNMeansPolicy);
                kernel.SetMemoryArgument(17, CB_BNStddevPolicy);
                kernel.SetMemoryArgument(18, CB_BNBetaPolicy);
                kernel.SetMemoryArgument(19, CB_BNGammaPolicy);
                kernel.SetMemoryArgument(20, CB_policyConnectionWeights);
                kernel.SetMemoryArgument(21, CB_policyBiases);
                kernel.SetMemoryArgument(22, CB_convFilterWeights);

                kernel.SetMemoryArgument(23, CB_output);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
    public class Job
    {
        public int globalID;

        public List<float> input = new List<float>();
        public List<float> output = new List<float>();

        public Job()
        { 

        }
    }
}
