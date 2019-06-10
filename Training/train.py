# -*- coding: utf-8 -*-
"""
@author ozzi7
"""

import os

from tictactoe_nn import *


class Trainer():
    def __init__(self):
        self.nnet = TicTacToeNet()
        self.EPOCHS = 1
        self.BATCH_SIZE = 100
        self.EPOCHS_FIT = 100

    def train(self, inputs, output_values, output_policies):
        """
        examples: list of examples, each example is of form (board, pi, v)
        """

        for eps in range(self.EPOCHS):
            print("Episode %d" % (eps))
            print("====================================================================================")

            self.nnet.model.fit([inputs], [output_policies, output_values],
                                     batch_size=self.BATCH_SIZE,
                                     epochs=self.EPOCHS_FIT,
                                     verbose=1)
            self.nnet.model.save("best_model.hd5f")

        self.nnet.dump_weights()

    def predict(self):
        self.nnet.model.predict()

    def save_checkpoint(self, folder='checkpoint', filename='checkpoint.pth.tar'):
        filepath = os.path.join(folder, filename)
        if not os.path.exists(folder):
            print("Checkpoint Directory does not exist! Making directory {}".format(folder))
            os.mkdir(folder)
        else:
            print("Checkpoint Directory exists! ")
        if self.saver == None:
            self.saver = tf.train.Saver(self.nnet.graph.get_collection('variables'))
        with self.nnet.graph.as_default():
            self.saver.save(self.sess, filepath)

    def load_checkpoint(self, folder='checkpoint', filename='checkpoint.pth.tar'):
        filepath = os.path.join(folder, filename)
        if not os.path.exists(filepath+'.meta'):
            raise("No model in path {}".format(filepath))
        with self.nnet.graph.as_default():
            self.saver = tf.train.Saver()
            self.saver.restore(self.sess, filepath)