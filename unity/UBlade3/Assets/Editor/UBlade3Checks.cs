using System;
using UnityEditor;
using UnityEngine;

namespace UBlade3.EditorTools
{
    // Batchmode acceptance entry point for the exact data/simulation slices.
    public static class UBlade3Checks
    {
        public static void RunAll()
        {
            var failed = false;
            try
            {
                var report = UBlade3.Data.DataProbe.Report();
                Debug.Log(report);
                if (!report.Contains("UBLADE3_DATA_OK")) failed = true;
            }
            catch (Exception exception)
            {
                Debug.Log("UBLADE3_DATA_FAILED " + exception);
                failed = true;
            }

            try
            {
                var report = UBlade3.Sim.SimSelfCheck.Report();
                Debug.Log(report);
                if (!UBlade3.Sim.SimSelfCheck.Check()) failed = true;
            }
            catch (Exception exception)
            {
                Debug.Log("UBLADE3_SIM_FAILED " + exception);
                failed = true;
            }

            if (failed)
            {
                Debug.Log("UBLADE3_CHECKS_FAILED");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("UBLADE3_CHECKS_OK");
            EditorApplication.Exit(0);
        }
    }
}
