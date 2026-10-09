FROM alpine:3.23
RUN apk add --no-cache iptables
COPY deploy/isolation/boundary.sh /boundary.sh
RUN sed -i 's/\r$//' /boundary.sh && chmod 0555 /boundary.sh
ENTRYPOINT ["/boundary.sh"]
