#!/bin/bash
# Background task script for running QQBot on cloud

screen -dmS dotbot bash -c "bash start_linux.sh"
screen -dmS napcat bash -c "napcat"
