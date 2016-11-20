﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsClient2.Protocol;
using DnsClient2.Protocol.Record;

namespace DnsClient2
{
    public abstract class DnsMessageHandler
    {
        public abstract Task<DnsResponseMessage> QueryAsync(DnsEndPoint server, DnsRequestMessage request, CancellationToken cancellationToken);

        protected virtual byte[] GetRequestData(DnsRequestMessage request)
        {
            var writer = new DnsDatagramWriter(DnsRequestHeader.HeaderLength);

            writer.SetInt16Network((short)request.Header.Id);
            writer.SetUInt16Network(request.Header.RawFlags);
            writer.SetInt16Network((short)request.Header.QuestionCount);

            // jump to end of header, we didn't write all fields
            writer.Index = DnsRequestHeader.HeaderLength;

            foreach (var question in request.Questions)
            {
                var questionData = question.QueryName.AsBytes();

                // 4 more bytes for the type and class
                writer.Extend(questionData.Length + 4);
                writer.SetBytes(questionData, questionData.Length);
                writer.SetUInt16Network(question.QuestionType);
                writer.SetUInt16Network(question.QuestionClass);
            }

            return writer.Data;
        }

        protected virtual DnsResponseMessage GetResponseMessage(byte[] responseData)
        {
            var reader = new DnsDatagramReader(responseData);
            var factory = new DnsRecordFactory(reader);

            var id = reader.ReadUInt16Reverse();
            var flags = reader.ReadUInt16Reverse();
            var questionCount = reader.ReadUInt16Reverse();
            var answerCount = reader.ReadUInt16Reverse();
            var nameServerCount = reader.ReadUInt16Reverse();
            var additionalCount = reader.ReadUInt16Reverse();

            var header = new DnsResponseHeader(id, flags, questionCount, answerCount, additionalCount, nameServerCount);
            var response = new DnsResponseMessage(header);

            for (int questionIndex = 0; questionIndex < questionCount; questionIndex++)
            {
                var question = new DnsQuestion(reader.ReadName(), reader.ReadUInt16Reverse(), reader.ReadUInt16Reverse());
                response.AddQuestion(question);
            }

            for (int answerIndex = 0; answerIndex < answerCount; answerIndex++)
            {
                ResourceRecordInfo info = ReadRecordInfo(reader);

                var record = factory.GetRecord(info);
                response.AddAnswer(record);
            }

            for (int serverIndex = 0; serverIndex < nameServerCount; serverIndex++)
            {
                ResourceRecordInfo info = ReadRecordInfo(reader);

                var record = factory.GetRecord(info);
                response.AddServer(record);
            }

            for (int additionalIndex = 0; additionalIndex < additionalCount; additionalIndex++)
            {
                ResourceRecordInfo info = ReadRecordInfo(reader);

                var record = factory.GetRecord(info);
                response.AddAdditional(record);
            }

            return response;
        }

        /*
        0  1  2  3  4  5  6  7  8  9  0  1  2  3  4  5
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
        |                                               |
        /                                               /
        /                      NAME                     /
        |                                               |
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
        |                      TYPE                     |
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
        |                     CLASS                     |
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
        |                      TTL                      |
        |                                               |
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
        |                   RDLENGTH                    |
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--|
        /                     RDATA                     /
        /                                               /
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
         * */
        private ResourceRecordInfo ReadRecordInfo(DnsDatagramReader reader)
        {
            return new ResourceRecordInfo(
                reader.ReadName().ToString(),                   // name
                reader.ReadUInt16Reverse(),                     // type
                reader.ReadUInt16Reverse(),                     // class
                reader.ReadUInt32Reverse(),                     // ttl - 32bit!!
                reader.ReadUInt16Reverse());                    // RDLength
                                                                 //reader.ReadBytes(reader.ReadUInt16Reverse()));  // rdata
        }
    }
}