# -*- coding: utf-8 -*-
"""
@author ozzi7

"""

import tensorflow as tf
from keras.backend.tensorflow_backend import set_session
from collections import deque
from trainer import Trainer
import numpy as np
from ast import literal_eval as createTuple
import re
import sys
import os
import glob
os.environ["TF_CPP_MIN_LOG_LEVEL"]="3"

MAX_FILES = 1


def read_samples(filename):
    # read training data from file
    with open(filename) as f:
        games = f.readlines()
        inputs = []
        output_values = []
        output_policies = []

        line_count = 0
        for line in games:
            # read value
            if line_count % 3 == 0:
                output_value = float(line)

            # read moves
            elif line_count % 3 == 1:
                # empty board first
                input = np.zeros((5, 5, 3))
                input[:, :, 2].fill(1)
                inputs.append(input)
                # add rotated boards
                # inputs.append(np.rot90(np.copy(input), axes=(1,0),k=1))
                # inputs.append(np.rot90(np.copy(input), axes=(1, 0), k=2))
                # inputs.append(np.rot90(np.copy(input), axes=(1, 0), k=3))

                pattern = '\((\d+, \d+)\)'
                data = re.findall(pattern, line)
                moves = []
                for item in data:
                    moves.append(tuple(map(lambda x: int(x), item.split(','))))

                # construct the input
                player = -1
                input = np.zeros((5, 5, 3))
                for i in range(len(moves)-1):
                    move = moves[i]

                    if player == 1:
                        input[move[0],move[1], 1] = 1 # y, x, channel
                        input[:, :,2].fill(1)

                    elif player == -1:
                        input[move[0], move[1],0] = 1
                        input[:, :,2].fill(0)

                    inputs.append(np.copy(input))
                    # add rotated boards
                    # inputs.append(np.rot90(np.copy(input), axes=(1, 0), k=1))
                    # inputs.append(np.rot90(np.copy(input), axes=(1, 0), k=2))
                    # inputs.append(np.rot90(np.copy(input), axes=(1, 0), k=3))

                    player *= -1

            # read policy
            elif line_count % 3 == 2:
                line = line.replace("(","").replace(")","").replace(",", " ")
                policies = [float(number) for number in line.split()]

                for move in range(len(moves)):
                    policy = np.zeros((25))
                    for i in range(25):
                        policy[i] = policies[move*25+i]

                    output_values.append(np.array([output_value])) # output val is from the view of player X
                    # output_values.append(np.array([output_value]))  # output val is from the view of player X
                    # output_values.append(np.array([output_value]))  # output val is from the view of player X
                    # output_values.append(np.array([output_value]))  # output val is from the view of player X

                    output_policies.append(policy)
                    # output_policies.append((np.rot90(np.reshape(np.copy(policy), (5, 5)),k=1)).flatten())
                    # output_policies.append((np.rot90(np.reshape(np.copy(policy), (5, 5)), k=2)).flatten())
                    # output_policies.append((np.rot90(np.reshape(np.copy(policy), (5, 5)), k=3)).flatten())

            line_count += 1

    return (inputs,output_values, output_policies)

if __name__ == '__main__':
    os.chdir(os.path.dirname(sys.argv[0]))

    list_of_files = glob.glob('./training_games*.txt')  # * means all if need specific format then *.csv
    files = sorted(list_of_files, key=lambda file: os.path.getctime(file),reverse=True)

    inputs = []
    output_values = []
    output_policies = []
    count = 0
    for file in files:
        if count < MAX_FILES:
            print(file)
            inputs_t,output_values_t, output_policies_t = read_samples(r'Z:/CloudStation/GitHub Projects/TicTacToe-DL-RL/Training/' + file)
            inputs.extend(inputs_t)
            output_values.extend(output_values_t)
            output_policies.extend(output_policies_t)
            count = count +1

    trainer = Trainer()
    #trainer.save_init_weights()
    #trainer.test_plot(inputs,output_values,output_policies)
    #trainer.train(inputs,output_values,output_policies)
    input("Press Enter to continue...")
    input("Press Enter to continue...")
    #(inputs, output_values, output_policies) = read_samples(r'Z:/CloudStation/GitHub Projects/TicTacToe-DL-RL/Training/' + sys.argv[1])
    #trainer.predict([inputs[7]])