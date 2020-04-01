{{/*
Set the common labels
*/}}
{{- define "baget.labels" -}}
app: {{ .Values.fullname }}
chart: {{ .Chart.Name }}
heritage: {{ .Release.Service | quote }}
release: {{ .Release.Name | quote }}
{{- end -}}

{{/*
Build image name
*/}}
{{- define "baget.image" -}}
{{- $image := default "loicsharma/baget" .Values.app.image -}}
{{- $imagetag := default "latest" .Values.app.imageVersion -}}
{{- printf "%s:%s" $image $imagetag -}}
{{- end -}}