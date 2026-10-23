using System;
using System.Collections.Generic;
using MOZA;
using UnityEngine;

public class MozaCarControllerSample : MonoBehaviour
{
    [SerializeField] private List<AxleInfo> axleInfos;
    [SerializeField] private float maxMotorTorque;
    [SerializeField] private float maxSteeringAngle;

    public void FixedUpdate()
    {
        if (!MozaWheelInput.IsConnected)
            return;

        var speed = 0f;
        if (MozaWheelInput.Throttle != 0)
        {
            speed = MozaWheelInput.Throttle;
        }
        else if (MozaWheelInput.Brake != 0)
        {
            speed = -MozaWheelInput.Brake;
        }

        var motor = maxMotorTorque * speed;
        var steering = maxSteeringAngle
            * MozaWheelInput.Steering;

        foreach (var axle in axleInfos)
        {
            if (axle.steering)
            {
                axle.leftWheel.steerAngle = steering;
                axle.rightWheel.steerAngle = steering;
            }

            if (axle.motor)
            {
                axle.leftWheel.motorTorque = motor;
                axle.rightWheel.motorTorque = motor;
            }
        }
    }

    [Serializable]
    public class AxleInfo
    {
        public WheelCollider leftWheel;
        public WheelCollider rightWheel;
        public bool motor;
        public bool steering;
    }
}
