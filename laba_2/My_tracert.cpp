#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <iostream>
#include <string>
#include <iomanip>
#include <windns.h>

#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "dnsapi.lib")

struct IcmpHeader {
    uint8_t type;
    uint8_t code;
    uint16_t checksum;
    uint16_t id;
    uint16_t seq;
};

uint16_t checksum(void* data, int len) {
    uint32_t sum = 0;
    uint16_t* ptr = (uint16_t*)data;
    while (len > 1) {
        sum += *ptr++;
        len -= 2;
    }
    if (len == 1) {
        uint16_t last = 0;
        *(uint8_t*)&last = *(uint8_t*)ptr;
        sum += last;
    }
    while (sum >> 16)
        sum = (sum & 0xFFFF) + (sum >> 16);
    return (uint16_t)(~sum);
}

std::string reverse_dns(const sockaddr_in& addr) {
    char ptrQuery[256];
    sprintf_s(ptrQuery, "%u.%u.%u.%u.in-addr.arpa",
        (unsigned)addr.sin_addr.S_un.S_un_b.s_b4,
        (unsigned)addr.sin_addr.S_un.S_un_b.s_b3,
        (unsigned)addr.sin_addr.S_un.S_un_b.s_b2,
        (unsigned)addr.sin_addr.S_un.S_un_b.s_b1);
    PDNS_RECORDA record = nullptr;
    DNS_STATUS status = DnsQuery_A(
        ptrQuery,
        DNS_TYPE_PTR,
        DNS_QUERY_STANDARD,
        nullptr,
        reinterpret_cast<PDNS_RECORD*>(&record),
        nullptr
    );
    if (status != 0 || record == nullptr) {
        if (record)
            DnsRecordListFree(record, DnsFreeRecordList);
        return "";
    }
    for (PDNS_RECORDA cur = record; cur != nullptr; cur = cur->pNext) {
        if (cur->wType == DNS_TYPE_PTR && cur->Data.PTR.pNameHost != nullptr) {
            std::string name(cur->Data.PTR.pNameHost);
            DnsRecordListFree(record, DnsFreeRecordList);
            return name;
        }
    }
    DnsRecordListFree(record, DnsFreeRecordList);
    return "";
}


int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cout << "Usage: mytraceroute [-n] <host>\n";
        return 1;
    }
    bool no_dns = false;
    std::string target;
    if (argc == 2) {
        target = argv[1];
    }
    else {
        if (std::string(argv[1]) == "-n") {
            no_dns = true;
            target = argv[2];
        }
        else {
            std::cout << "Usage: mytraceroute [-n] <host>\n";
            return 1;
        }
    }
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) {
        std::cerr << "WSAStartup failed\n";
        return 1;
    }
    addrinfo hints{};
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_RAW;
    hints.ai_protocol = IPPROTO_ICMP;
    addrinfo* result = nullptr;
    int r = getaddrinfo(target.c_str(), nullptr, &hints, &result);
    if (r != 0 || !result) {
        std::cerr << "getaddrinfo failed: " << r << "\n";
        WSACleanup();
        return 1;
    }
    sockaddr_in dest = *(sockaddr_in*)result->ai_addr;
    char destStr[INET_ADDRSTRLEN];
    inet_ntop(AF_INET, &dest.sin_addr, destStr, sizeof(destStr));
    std::cout << "Tracing route to " << target << " [" << destStr << "]\n\n";
    SOCKET sock = socket(AF_INET, SOCK_RAW, IPPROTO_ICMP);
    if (sock == INVALID_SOCKET) {
        std::cerr << "socket failed: " << WSAGetLastError() << "\n";
        freeaddrinfo(result);
        WSACleanup();
        return 1;
    }
    int timeout = 3000;
    setsockopt(sock, SOL_SOCKET, SO_RCVTIMEO, (char*)&timeout, sizeof(timeout));
    const int maxHops = 30;
    const int probesPerHop = 3;
    uint16_t pid = (uint16_t)GetCurrentProcessId();
    uint16_t seq = 1;
    for (int ttl = 1; ttl <= maxHops; ++ttl) {
        if (setsockopt(sock, IPPROTO_IP, IP_TTL, (char*)&ttl, sizeof(ttl)) == SOCKET_ERROR) {
            std::cerr << "setsockopt(IP_TTL) failed: " << WSAGetLastError() << "\n";
            break;
        }
        std::cout << std::setw(2) << ttl << "  ";
        bool reached = false;
        sockaddr_in lastResponder{};
        bool gotAny = false;
        for (int p = 0; p < probesPerHop; ++p, ++seq) {
            char sendBuf[64]{};
            IcmpHeader* icmp = (IcmpHeader*)sendBuf;
            icmp->type = 8;
            icmp->code = 0;
            icmp->checksum = 0;
            icmp->id = htons(pid);
            icmp->seq = htons(seq);
            const char* payload = "mytraceroute";
            int payloadLen = (int)strlen(payload);
            memcpy(sendBuf + sizeof(IcmpHeader), payload, payloadLen);
            int packetLen = sizeof(IcmpHeader) + payloadLen;
            icmp->checksum = checksum(sendBuf, packetLen);
            auto start = GetTickCount();
            int sent = sendto(sock, sendBuf, packetLen, 0,
                (sockaddr*)&dest, sizeof(dest));
            if (sent == SOCKET_ERROR) {
                std::cout << " * ";
                continue;
            }
            char recvBuf[1024];
            sockaddr_in from{};
            int fromLen = sizeof(from);
            int recvd = recvfrom(sock, recvBuf, sizeof(recvBuf), 0,
                (sockaddr*)&from, &fromLen);
            if (recvd == SOCKET_ERROR) {
                std::cout << " * ";
                continue;
            }
            auto end = GetTickCount();
            int rtt = (int)(end - start);
            gotAny = true;
            lastResponder = from;
            unsigned char* ipHdr = (unsigned char*)recvBuf;
            int ipHdrLen = (ipHdr[0] & 0x0F) * 4;
            if (recvd < ipHdrLen + sizeof(IcmpHeader)) {
                std::cout << " ? ";
                continue;
            }
            IcmpHeader* ricmp = (IcmpHeader*)(recvBuf + ipHdrLen);
            if (ricmp->type == 0 && ricmp->code == 0) {
                reached = true;
            }
            std::cout << std::setw(3) << rtt << "ms ";
        }
        if (gotAny) {
            char addrStr[INET_ADDRSTRLEN];
            inet_ntop(AF_INET, &lastResponder.sin_addr, addrStr, sizeof(addrStr));
            std::cout << " " << addrStr;
            if (!no_dns) {
                std::string name = reverse_dns(lastResponder);
                if (!name.empty())
                    std::cout << " [" << name << "]";
            }
        }
        std::cout << "\n";
        if (reached)
            break;
    }

    closesocket(sock);
    freeaddrinfo(result);
    WSACleanup();
    return 0;
}